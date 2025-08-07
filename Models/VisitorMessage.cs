using System.ComponentModel.DataAnnotations;

namespace GapInMyResume.API.Models
{
    public class VisitorMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [StringLength(2000)]
        public string Message { get; set; } = string.Empty;
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public bool IsRead { get; set; } = false;
    }
    
    public class CreateVisitorMessageDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [StringLength(2000)]
        public string Message { get; set; } = string.Empty;
    }
}