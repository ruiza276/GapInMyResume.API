using Microsoft.AspNetCore.Mvc;
using GapInMyResume.API.Services;

namespace GapInMyResume.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly IBlobStorageService _blobStorageService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FilesController> _logger;

        public FilesController(
            IBlobStorageService blobStorageService,
            IConfiguration configuration,
            ILogger<FilesController> logger)
        {
            _blobStorageService = blobStorageService;
            _configuration = configuration;
            _logger = logger;
        }

        // POST: api/files/upload-image
        [HttpPost("upload-image")]
        public async Task<ActionResult<object>> UploadImage(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file provided");
                }

                // Validate file type
                if (!IsImageFile(file))
                {
                    return BadRequest("Only image files are allowed");
                }

                // Validate file size (e.g., max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return BadRequest("File size cannot exceed 10MB");
                }

                var containerName = _configuration["BlobStorage:ImagesContainer"];
                var fileUrl = await _blobStorageService.UploadFileAsync(file, containerName!);

                return Ok(new
                {
                    url = fileUrl,
                    fileName = file.FileName,
                    size = file.Length,
                    contentType = file.ContentType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/files/upload-text
        [HttpPost("upload-text")]
        public async Task<ActionResult<object>> UploadTextFile(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file provided");
                }

                // Validate file type
                if (!IsTextFile(file))
                {
                    return BadRequest("Only text files are allowed");
                }

                // Validate file size (e.g., max 5MB)
                if (file.Length > 5 * 1024 * 1024)
                {
                    return BadRequest("File size cannot exceed 5MB");
                }

                var containerName = _configuration["BlobStorage:TextFilesContainer"];
                var fileUrl = await _blobStorageService.UploadFileAsync(file, containerName!);

                return Ok(new
                {
                    url = fileUrl,
                    fileName = file.FileName,
                    size = file.Length,
                    contentType = file.ContentType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading text file");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/files/download/{containerName}/{fileName}
        [HttpGet("download/{containerName}/{fileName}")]
        public async Task<IActionResult> DownloadFile(string containerName, string fileName)
        {
            try
            {
                // Validate container name for security
                var allowedContainers = new[] { 
                    _configuration["BlobStorage:ImagesContainer"], 
                    _configuration["BlobStorage:TextFilesContainer"] 
                };
                
                if (!allowedContainers.Contains(containerName))
                {
                    return BadRequest("Invalid container name");
                }

                var fileStream = await _blobStorageService.DownloadFileAsync(fileName, containerName);
                
                // Determine content type based on file extension
                var contentType = GetContentType(fileName);
                
                return File(fileStream, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                return NotFound();
            }
        }

        private static bool IsImageFile(IFormFile file)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return imageExtensions.Contains(extension);
        }

        private static bool IsTextFile(IFormFile file)
        {
            var textExtensions = new[] { ".txt", ".md", ".json", ".xml", ".csv", ".log" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return textExtensions.Contains(extension);
        }

        private static string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".csv" => "text/csv",
                ".log" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}