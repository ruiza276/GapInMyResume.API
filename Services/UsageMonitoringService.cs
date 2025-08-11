// Services/UsageMonitoringService.cs
// Service to monitor Azure free tier usage

namespace GapInMyResume.API.Services
{
    public class UsageMonitoringService
    {
        private readonly ILogger<UsageMonitoringService> _logger;
        private static long _dailyRequests = 0;
        private static long _dailyBandwidth = 0;
        private static DateTime _lastReset = DateTime.UtcNow.Date;
        private static readonly object _lock = new object();

        // Azure Free Tier Limits
        private const long MAX_DAILY_BANDWIDTH = 165 * 1024 * 1024; // 165 MB
        private const long MAX_DAILY_CPU_MINUTES = 60; // 60 minutes
        private const long WARNING_THRESHOLD_BANDWIDTH = (long)(MAX_DAILY_BANDWIDTH * 0.8); // 80%
        private const long CRITICAL_THRESHOLD_BANDWIDTH = (long)(MAX_DAILY_BANDWIDTH * 0.95); // 95%

        public UsageMonitoringService(ILogger<UsageMonitoringService> logger)
        {
            _logger = logger;
        }

        public void TrackRequest(long responseSize = 0, TimeSpan? processingTime = null)
        {
            lock (_lock)
            {
                // Reset counters daily
                if (DateTime.UtcNow.Date > _lastReset)
                {
                    _dailyRequests = 0;
                    _dailyBandwidth = 0;
                    _lastReset = DateTime.UtcNow.Date;
                    _logger.LogInformation("Daily usage counters reset");
                }

                Interlocked.Increment(ref _dailyRequests);
                Interlocked.Add(ref _dailyBandwidth, responseSize);

                // Check bandwidth warnings
                CheckBandwidthLimits();

                // Log periodic stats
                if (_dailyRequests % 50 == 0) // Log every 50 requests
                {
                    LogUsageStats();
                }

                // Log slow requests
                if (processingTime.HasValue && processingTime.Value.TotalSeconds > 5)
                {
                    _logger.LogWarning($"Slow request detected: {processingTime.Value.TotalSeconds:F2}s");
                }
            }
        }

        private void CheckBandwidthLimits()
        {
            var bandwidthMB = _dailyBandwidth / (1024.0 * 1024.0);
            var maxBandwidthMB = MAX_DAILY_BANDWIDTH / (1024.0 * 1024.0);
            var percentageUsed = (_dailyBandwidth * 100.0) / MAX_DAILY_BANDWIDTH;

            if (_dailyBandwidth > CRITICAL_THRESHOLD_BANDWIDTH)
            {
                _logger.LogCritical($"CRITICAL: Daily bandwidth usage at {percentageUsed:F1}% " +
                                  $"({bandwidthMB:F1} MB / {maxBandwidthMB:F1} MB). Consider optimizations!");
            }
            else if (_dailyBandwidth > WARNING_THRESHOLD_BANDWIDTH)
            {
                _logger.LogWarning($"WARNING: Daily bandwidth usage at {percentageUsed:F1}% " +
                                 $"({bandwidthMB:F1} MB / {maxBandwidthMB:F1} MB)");
            }
        }

        private void LogUsageStats()
        {
            var bandwidthMB = _dailyBandwidth / (1024.0 * 1024.0);
            var maxBandwidthMB = MAX_DAILY_BANDWIDTH / (1024.0 * 1024.0);
            var percentageUsed = (_dailyBandwidth * 100.0) / MAX_DAILY_BANDWIDTH;

            _logger.LogInformation($"Daily Usage Stats - " +
                                 $"Requests: {_dailyRequests}, " +
                                 $"Bandwidth: {bandwidthMB:F1} MB / {maxBandwidthMB:F1} MB ({percentageUsed:F1}%)");
        }

        public UsageStats GetCurrentUsage()
        {
            lock (_lock)
            {
                return new UsageStats
                {
                    Date = _lastReset,
                    RequestCount = _dailyRequests,
                    BandwidthUsed = _dailyBandwidth,
                    BandwidthLimit = MAX_DAILY_BANDWIDTH,
                    BandwidthPercentage = (_dailyBandwidth * 100.0) / MAX_DAILY_BANDWIDTH
                };
            }
        }

        public bool IsNearingLimits()
        {
            return _dailyBandwidth > WARNING_THRESHOLD_BANDWIDTH;
        }

        public void LogStartupInfo()
        {
            _logger.LogInformation($"Usage Monitoring Service initialized. " +
                                 $"Daily limits - Bandwidth: {MAX_DAILY_BANDWIDTH / (1024 * 1024)} MB, " +
                                 $"CPU: {MAX_DAILY_CPU_MINUTES} minutes");
        }
    }

    public class UsageStats
    {
        public DateTime Date { get; set; }
        public long RequestCount { get; set; }
        public long BandwidthUsed { get; set; }
        public long BandwidthLimit { get; set; }
        public double BandwidthPercentage { get; set; }
    }
}