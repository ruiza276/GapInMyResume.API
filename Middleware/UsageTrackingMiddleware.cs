// Middleware/UsageTrackingMiddleware.cs
// Middleware to track bandwidth and performance usage

using GapInMyResume.API.Services;
using System.Diagnostics;

namespace GapInMyResume.API.Middleware
{
    public class UsageTrackingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UsageTrackingMiddleware> _logger;

        public UsageTrackingMiddleware(RequestDelegate next, ILogger<UsageTrackingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, UsageMonitoringService usageMonitor)
        {
            var stopwatch = Stopwatch.StartNew();
            var originalBodyStream = context.Response.Body;

            try
            {
                using var responseBody = new MemoryStream();
                context.Response.Body = responseBody;

                // Call the next middleware
                await _next(context);

                stopwatch.Stop();

                // Track usage metrics
                usageMonitor.TrackRequest(responseBody.Length, stopwatch.Elapsed);

                // Log detailed info for API endpoints
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    _logger.LogInformation($"API Request: {context.Request.Method} {context.Request.Path} " +
                                         $"- Status: {context.Response.StatusCode}, " +
                                         $"Size: {responseBody.Length} bytes, " +
                                         $"Time: {stopwatch.ElapsedMilliseconds}ms");
                }

                // Check if we're nearing limits and log warnings
                if (usageMonitor.IsNearingLimits())
                {
                    _logger.LogWarning("Application is nearing Azure free tier limits. Consider optimizations.");
                }

                // Copy response back to original stream
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, $"Error in request processing: {context.Request.Path}");
                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }
    }

    // Extension method for easy registration
    public static class UsageTrackingMiddlewareExtensions
    {
        public static IApplicationBuilder UseUsageTracking(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UsageTrackingMiddleware>();
        }
    }
}