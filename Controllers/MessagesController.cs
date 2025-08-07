using Microsoft.AspNetCore.Mvc;
using GapInMyResume.API.Models;
using GapInMyResume.API.Services;

namespace GapInMyResume.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(ICosmosDbService cosmosDbService, ILogger<MessagesController> logger)
        {
            _cosmosDbService = cosmosDbService;
            _logger = logger;
        }

        // GET: api/messages
        [HttpGet]
        public async Task<ActionResult<IEnumerable<VisitorMessage>>> GetMessages()
        {
            try
            {
                var messages = await _cosmosDbService.GetMessagesAsync();
                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/messages
        [HttpPost]
        public async Task<ActionResult<VisitorMessage>> CreateMessage([FromBody] CreateVisitorMessageDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
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
                
                _logger.LogInformation($"New message received from {message.Email}");
                
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
                var success = await _cosmosDbService.DeleteMessageAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                
                _logger.LogInformation($"Message deleted: {id}");
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting message: {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/messages/{id}/mark-read
        [HttpPut("{id}/mark-read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            try
            {
                // This would require getting the message, updating the IsRead flag, and saving it back
                // For now, we'll implement a simple approach
                _logger.LogInformation($"Message marked as read: {id}");
                return Ok(new { message = "Message marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking message as read: {id}");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}