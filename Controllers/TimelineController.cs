using Microsoft.AspNetCore.Mvc;
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
        private readonly ILogger<TimelineController> _logger;

        public TimelineController(
            ICosmosDbService cosmosDbService,
            IBlobStorageService blobStorageService,
            IConfiguration configuration,
            ILogger<TimelineController> logger)
        {
            _cosmosDbService = cosmosDbService;
            _blobStorageService = blobStorageService;
            _configuration = configuration;
            _logger = logger;
        }

        // GET: api/timeline
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TimelineItem>>> GetTimelineItems()
        {
            try
            {
                var items = await _cosmosDbService.GetTimelineItemsAsync();
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

        // GET: api/timeline/date/{date}
        [HttpGet("date/{date}")]
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

                // Get all timeline items and filter by date
                var allItems = await _cosmosDbService.GetTimelineItemsAsync();
                _logger.LogInformation($"Found {allItems.Count()} total timeline items");
                
                // Log all dates for debugging
                foreach (var item in allItems)
                {
                    _logger.LogInformation($"Timeline item date: {item.Date:yyyy-MM-dd} (comparing with {parsedDate:yyyy-MM-dd})");
                }
                
                var matchingItem = allItems.FirstOrDefault(x => x.Date.Date == parsedDate.Date);
                
                if (matchingItem == null)
                {
                    _logger.LogInformation($"No timeline item found for date: {parsedDate:yyyy-MM-dd}");
                    return NotFound($"No timeline item found for date: {parsedDate:yyyy-MM-dd}");
                }
                
                _logger.LogInformation($"Found timeline item: {matchingItem.Title} for date: {parsedDate:yyyy-MM-dd}");
                return Ok(matchingItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving timeline item for date: {date}");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/timeline
        [HttpPost]
        public async Task<ActionResult<TimelineItem>> CreateTimelineItem([FromForm] CreateTimelineItemDto dto)
        {
            try
            {
                var timelineItem = new TimelineItem
                {
                    Date = dto.Date,
                    Title = dto.Title,
                    Description = dto.Description,
                    CreatedAt = DateTime.UtcNow
                };

                // Handle file upload if present
                if (dto.File != null && dto.File.Length > 0)
                {
                    // Determine container based on file type
                    var containerName = IsImageFile(dto.File) 
                        ? _configuration["BlobStorage:ImagesContainer"] 
                        : _configuration["BlobStorage:TextFilesContainer"];

                    // Upload file to blob storage
                    var fileUrl = await _blobStorageService.UploadFileAsync(dto.File, containerName!);
                    
                    timelineItem.FileUrl = fileUrl;
                    timelineItem.FileName = dto.File.FileName;
                    timelineItem.FileType = IsImageFile(dto.File) ? "image" : "text";
                }

                var createdItem = await _cosmosDbService.CreateTimelineItemAsync(timelineItem);
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
                    var containerName = IsImageFile(dto.File) 
                        ? _configuration["BlobStorage:ImagesContainer"] 
                        : _configuration["BlobStorage:TextFilesContainer"];

                    var fileUrl = await _blobStorageService.UploadFileAsync(dto.File, containerName!);
                    
                    existingItem.FileUrl = fileUrl;
                    existingItem.FileName = dto.File.FileName;
                    existingItem.FileType = IsImageFile(dto.File) ? "image" : "text";
                }

                var updatedItem = await _cosmosDbService.UpdateTimelineItemAsync(id, existingItem);
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
                var success = await _cosmosDbService.DeleteTimelineItemAsync(id);
                if (!success)
                {
                    return NotFound();
                }
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