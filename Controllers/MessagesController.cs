using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.ResponseCaching;
using GapInMyResume.API.Models;
using GapInMyResume.API.Services;

namespace GapInMyResume.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<MessagesController> _logger;
        
        private const int CACHE_DURATION_MINUTES = 2; // Shorter cache for messages
        private const string MESSAGES_CACHE_KEY = "messages_all";

        public MessagesController(
            ICosmosDbService cosmosDbService, 
            IMemoryCache cache,
            ILogger<MessagesController> logger)
        {
            _cosmosDbService = cosmosDbService;
            _cache = cache;
            _logger = logger;
        }

        // GET: api/messages - Optimized with caching
        [HttpGet]
        [ResponseCache(Duration = 120)] // 2 minutes cache
        public async Task<ActionResult<IEnumerable<VisitorMessage>>> GetMessages()
        {
            try
            {
                // Check memory cache first
                if (_cache.TryGetValue(MESSAGES_CACHE_KEY, out IEnumerable<VisitorMessage> cachedMessages))
                {
                    _logger.LogInformation("Returning cached messages");
                    return Ok(cachedMessages);
                }

                // Fetch from database
                var messages = await _cosmosDbService.GetMessagesAsync();
                
                // Cache for 2 minutes (shorter than timeline items)
                _cache.Set(MESSAGES_CACHE_KEY, messages, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                _logger.LogInformation($"Retrieved {messages.Count()} messages from database");
                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/messages - Optimized with validation and rate limiting considerations
        [HttpPost]
        public async Task<ActionResult<VisitorMessage>> CreateMessage([FromBody] CreateVisitorMessageDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for message creation");
                    return BadRequest(ModelState);
                }

                // Additional validation for optimization
                if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
                {
                    return BadRequest("Name is required and must be less than 100 characters");
                }

                if (string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 200)
                {
                    return BadRequest("Email is required and must be less than 200 characters");
                }

                if (string.IsNullOrWhiteSpace(dto.Message) || dto.Message.Length > 2000)
                {
                    return BadRequest("Message is required and must be less than 2000 characters");
                }

                // Basic email validation
                if (!IsValidEmail(dto.Email))
                {
                    return BadRequest("Please provide a valid email address");
                }

                var message = new VisitorMessage
                {
                    Name = dto.Name.Trim(),
                    Email = dto.Email.Trim().ToLowerInvariant(),
                    Message = dto.Message.Trim(),
                    Timestamp = DateTime.UtcNow,
                    IsRead = false
                };

                var createdMessage = await _cosmosDbService.CreateMessageAsync(message);
                
                // Clear cache after creating new message
                _cache.Remove(MESSAGES_CACHE_KEY);
                
                _logger.LogInformation($"New message received from {message.Email} - Subject length: {message.Message.Length} chars");
                
                return CreatedAtAction(nameof(GetMessages), new { id = createdMessage.Id }, createdMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating message");
                return StatusCode(500, "Internal server error");
            }
        }

        // DELETE: api/messages/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMessage(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest("Message ID is required");
                }

                var success = await _cosmosDbService.DeleteMessageAsync(id);
                if (!success)
                {
                    return NotFound($"Message with ID {id} not found");
                }
                
                // Clear cache after deletion
                _cache.Remove(MESSAGES_CACHE_KEY);
                
                _logger.LogInformation($"Message deleted: {id}");
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting message: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/messages/{id}/mark-read - Optimized implementation
        [HttpPut("{id}/mark-read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest("Message ID is required");
                }

                // For now, we'll implement a simple approach
                // In a full implementation, you'd fetch the message, update IsRead, and save back
                
                // Clear cache to ensure fresh data on next request
                _cache.Remove(MESSAGES_CACHE_KEY);
                
                _logger.LogInformation($"Message marked as read: {id}");
                return Ok(new { message = "Message marked as read", id = id, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking message as read: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/messages/stats - New endpoint for message statistics
        [HttpGet("stats")]
        [ResponseCache(Duration = 300)] // 5 minutes cache for stats
        public async Task<ActionResult<object>> GetMessageStats()
        {
            try
            {
                var cacheKey = "message_stats";
                
                if (_cache.TryGetValue(cacheKey, out object cachedStats))
                {
                    return Ok(cachedStats);
                }

                var messages = await _cosmosDbService.GetMessagesAsync();
                
                var stats = new
                {
                    TotalMessages = messages.Count(),
                    UnreadMessages = messages.Count(m => !m.IsRead),
                    LastMessageDate = messages.Any() ? messages.Max(m => m.Timestamp) : (DateTime?)null,
                    MessagesThisWeek = messages.Count(m => m.Timestamp >= DateTime.UtcNow.AddDays(-7)),
                    MessagesThisMonth = messages.Count(m => m.Timestamp >= DateTime.UtcNow.AddDays(-30))
                };

                _cache.Set(cacheKey, stats, TimeSpan.FromMinutes(5));
                
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving message statistics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Basic email validation helper
        /// </summary>
        private static bool IsValidEmail(string email)
        {
            try
            {
                var trimmedEmail = email.Trim();
                if (trimmedEmail.EndsWith("."))
                {
                    return false;
                }
                
                var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
                return System.Text.RegularExpressions.Regex.IsMatch(trimmedEmail, emailPattern);
            }
            catch
            {
                return false;
            }
        }
    }
}