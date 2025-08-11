using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.ResponseCaching;
using GapInMyResume.API.Models;
using GapInMyResume.API.Services;

namespace GapInMyResume.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimelineController : ControllerBase
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TimelineController> _logger;
        
        private const int CACHE_DURATION_MINUTES = 5;
        private const string TIMELINE_CACHE_KEY = "timeline_items";

        public TimelineController(
            ICosmosDbService cosmosDbService,
            IBlobStorageService blobStorageService,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<TimelineController> logger)
        {
            _cosmosDbService = cosmosDbService;
            _blobStorageService = blobStorageService;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
        }

        // GET: api/timeline - Optimized with caching
        [HttpGet]
        [ResponseCache(Duration = 300)] // 5 minutes
        public async Task<ActionResult<IEnumerable<TimelineItem>>> GetTimelineItems()
        {
            try
            {
                // Check memory cache first
                if (_cache.TryGetValue(TIMELINE_CACHE_KEY, out IEnumerable<TimelineItem> cachedItems))
                {
                    _logger.LogInformation("Returning cached timeline items");
                    return Ok(cachedItems);
                }

                // Fetch from database
                var items = await _cosmosDbService.GetTimelineItemsAsync();
                
                // Cache for 5 minutes
                _cache.Set(TIMELINE_CACHE_KEY, items, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                _logger.LogInformation($"Retrieved {items.Count()} timeline items from database");
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timeline items");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/timeline/{id}
        [HttpGet("{id}")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<TimelineItem>> GetTimelineItem(string id)
        {
            try
            {
                var item = await _cosmosDbService.GetTimelineItemAsync(id);
                if (item == null)
                {
                    return NotFound();
                }
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving timeline item: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/timeline/date/{date} - Optimized with caching
        [HttpGet("date/{date}")]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "date" })]
        public async Task<ActionResult<TimelineItem>> GetTimelineItemByDate(string date)
        {
            try
            {
                _logger.LogInformation($"Searching for timeline item with date: {date}");
                
                // Parse the date string to DateTime
                if (!DateTime.TryParse(date, out DateTime parsedDate))
                {
                    _logger.LogWarning($"Invalid date format received: {date}");
                    return BadRequest("Invalid date format. Expected format: YYYY-MM-DD");
                }

                var cacheKey = $"timeline_date_{parsedDate:yyyy-MM-dd}";
                
                // Check memory cache first
                if (_cache.TryGetValue(cacheKey, out TimelineItem cachedItem))
                {
                    _logger.LogInformation($"Returning cached timeline item for date: {date}");
                    return Ok(cachedItem);
                }

                // Get all timeline items and filter by date
                var allItems = await _cosmosDbService.GetTimelineItemsAsync();
                _logger.LogInformation($"Found {allItems.Count()} total timeline items");
                
                var matchingItem = allItems.FirstOrDefault(x => x.Date.Date == parsedDate.Date);
                
                if (matchingItem == null)
                {
                    _logger.LogInformation($"No timeline item found for date: {parsedDate:yyyy-MM-dd}");
                    return NotFound($"No timeline item found for date: {parsedDate:yyyy-MM-dd}");
                }

                // Cache for 5 minutes
                _cache.Set(cacheKey, matchingItem, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                _logger.LogInformation($"Found timeline item: {matchingItem.Title} for date: {parsedDate:yyyy-MM-dd}");
                return Ok(matchingItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving timeline item for date: {date}");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/timeline - Optimized with file validation
        [HttpPost]
        public async Task<ActionResult<TimelineItem>> CreateTimelineItem([FromForm] CreateTimelineItemDto dto)
        {
            try
            {
                _logger.LogInformation("Creating new timeline item");

                var timelineItem = new TimelineItem
                {
                    Date = dto.Date,
                    Title = dto.Title,
                    Description = dto.Description,
                    CreatedAt = DateTime.UtcNow
                };

                // Handle file upload if present with optimization
                if (dto.File != null && dto.File.Length > 0)
                {
                    // Validate file size (limit to 5MB to stay within free tier)
                    const long maxFileSize = 5 * 1024 * 1024; // 5MB
                    if (dto.File.Length > maxFileSize)
                    {
                        return BadRequest("File size must be less than 5MB");
                    }

                    // Validate file type
                    var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "text/plain", "application/pdf" };
                    if (!allowedTypes.Contains(dto.File.ContentType))
                    {
                        return BadRequest("File type not supported");
                    }

                    // Determine container based on file type
                    var containerName = IsImageFile(dto.File) 
                        ? _configuration["BlobStorage:ImagesContainer"] 
                        : _configuration["BlobStorage:TextFilesContainer"];

                    _logger.LogInformation($"Uploading file: {dto.File.FileName} ({dto.File.Length} bytes)");

                    // Upload file to blob storage
                    var fileUrl = await _blobStorageService.UploadFileAsync(dto.File, containerName!);
                    
                    timelineItem.FileUrl = fileUrl;
                    timelineItem.FileName = dto.File.FileName;
                    timelineItem.FileType = IsImageFile(dto.File) ? "image" : "text";
                }

                var createdItem = await _cosmosDbService.CreateTimelineItemAsync(timelineItem);

                // Clear cache after creating new item
                _cache.Remove(TIMELINE_CACHE_KEY);
                _cache.Remove($"timeline_date_{dto.Date:yyyy-MM-dd}");

                _logger.LogInformation($"Created timeline item: {createdItem.Title} for date: {createdItem.Date:yyyy-MM-dd}");
                
                return CreatedAtAction(nameof(GetTimelineItem), new { id = createdItem.Id }, createdItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating timeline item");
                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/timeline/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<TimelineItem>> UpdateTimelineItem(string id, [FromForm] CreateTimelineItemDto dto)
        {
            try
            {
                var existingItem = await _cosmosDbService.GetTimelineItemAsync(id);
                if (existingItem == null)
                {
                    return NotFound();
                }

                // Update properties
                existingItem.Date = dto.Date;
                existingItem.Title = dto.Title;
                existingItem.Description = dto.Description;

                // Handle file upload if present
                if (dto.File != null && dto.File.Length > 0)
                {
                    // Validate file size
                    const long maxFileSize = 5 * 1024 * 1024; // 5MB
                    if (dto.File.Length > maxFileSize)
                    {
                        return BadRequest("File size must be less than 5MB");
                    }

                    var containerName = IsImageFile(dto.File) 
                        ? _configuration["BlobStorage:ImagesContainer"] 
                        : _configuration["BlobStorage:TextFilesContainer"];

                    var fileUrl = await _blobStorageService.UploadFileAsync(dto.File, containerName!);
                    
                    existingItem.FileUrl = fileUrl;
                    existingItem.FileName = dto.File.FileName;
                    existingItem.FileType = IsImageFile(dto.File) ? "image" : "text";
                }

                var updatedItem = await _cosmosDbService.UpdateTimelineItemAsync(id, existingItem);

                // Clear relevant cache entries
                _cache.Remove(TIMELINE_CACHE_KEY);
                _cache.Remove($"timeline_date_{existingItem.Date:yyyy-MM-dd}");

                return Ok(updatedItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating timeline item: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // DELETE: api/timeline/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTimelineItem(string id)
        {
            try
            {
                var item = await _cosmosDbService.GetTimelineItemAsync(id);
                if (item == null)
                {
                    return NotFound();
                }

                var success = await _cosmosDbService.DeleteTimelineItemAsync(id);
                if (!success)
                {
                    return NotFound();
                }

                // Clear cache
                _cache.Remove(TIMELINE_CACHE_KEY);
                _cache.Remove($"timeline_date_{item.Date:yyyy-MM-dd}");

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting timeline item: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        private static bool IsImageFile(IFormFile file)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return imageExtensions.Contains(extension);
        }
    }
}