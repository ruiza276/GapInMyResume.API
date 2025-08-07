using System.ComponentModel.DataAnnotations;

namespace GapInMyResume.API.Models
{
    public class TimelineItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public DateTime Date { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;
        
        public string? FileUrl { get; set; }
        
        [StringLength(10)]
        public string FileType { get; set; } = string.Empty; // "image" or "text"
        
        public string? FileName { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class CreateTimelineItemDto
    {
        [Required]
        public DateTime Date { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;
        
        public IFormFile? File { get; set; }
    }
}