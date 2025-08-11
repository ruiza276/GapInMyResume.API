using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCaching;
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

        // POST: api/files/upload-image - Optimized with file size limits
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

                // Optimized file size validation (reduced for free tier)
                const long maxFileSize = 5 * 1024 * 1024; // 5MB instead of 10MB
                if (file.Length > maxFileSize)
                {
                    return BadRequest("File size cannot exceed 5MB to optimize storage usage");
                }

                // Additional validation for image dimensions could be added here
                if (file.Length < 100) // Minimum file size check
                {
                    return BadRequest("File appears to be empty or corrupted");
                }

                var containerName = _configuration["BlobStorage:ImagesContainer"];
                
                _logger.LogInformation($"Uploading image: {file.FileName} ({file.Length} bytes)");
                
                var fileUrl = await _blobStorageService.UploadFileAsync(file, containerName!);

                var response = new
                {
                    url = fileUrl,
                    fileName = file.FileName,
                    size = file.Length,
                    contentType = file.ContentType,
                    uploadedAt = DateTime.UtcNow,
                    container = containerName
                };

                _logger.LogInformation($"Image uploaded successfully: {file.FileName} -> {fileUrl}");

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading image: {file?.FileName}");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/files/upload-text - Optimized with file size limits
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

                // Optimized file size validation (reduced for free tier)
                const long maxFileSize = 2 * 1024 * 1024; // 2MB for text files
                if (file.Length > maxFileSize)
                {
                    return BadRequest("File size cannot exceed 2MB for text files");
                }

                if (file.Length < 1) // Minimum file size check
                {
                    return BadRequest("File appears to be empty");
                }

                var containerName = _configuration["BlobStorage:TextFilesContainer"];
                
                _logger.LogInformation($"Uploading text file: {file.FileName} ({file.Length} bytes)");
                
                var fileUrl = await _blobStorageService.UploadFileAsync(file, containerName!);

                var response = new
                {
                    url = fileUrl,
                    fileName = file.FileName,
                    size = file.Length,
                    contentType = file.ContentType,
                    uploadedAt = DateTime.UtcNow,
                    container = containerName
                };

                _logger.LogInformation($"Text file uploaded successfully: {file.FileName} -> {fileUrl}");

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading text file: {file?.FileName}");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/files/download/{containerName}/{fileName} - Optimized with caching
        [HttpGet("download/{containerName}/{fileName}")]
        [ResponseCache(Duration = 3600)] // Cache downloads for 1 hour
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
                    _logger.LogWarning($"Attempted access to invalid container: {containerName}");
                    return BadRequest("Invalid container name");
                }

                // Additional security: validate filename
                if (string.IsNullOrWhiteSpace(fileName) || 
                    fileName.Contains("..") || 
                    fileName.Contains("/") || 
                    fileName.Contains("\\"))
                {
                    _logger.LogWarning($"Attempted access to invalid filename: {fileName}");
                    return BadRequest("Invalid filename");
                }

                _logger.LogInformation($"Downloading file: {fileName} from {containerName}");

                var fileStream = await _blobStorageService.DownloadFileAsync(fileName, containerName);
                
                // Determine content type based on file extension
                var contentType = GetContentType(fileName);
                
                // Add cache headers for better performance
                Response.Headers.Add("Cache-Control", "public, max-age=3600");
                Response.Headers.Add("ETag", $"\"{fileName}-{DateTime.UtcNow:yyyyMMdd}\"");
                
                return File(fileStream, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName} from {containerName}");
                return NotFound($"File not found: {fileName}");
            }
        }

        // GET: api/files/info/{containerName} - New endpoint for container info
        [HttpGet("info/{containerName}")]
        [ResponseCache(Duration = 600)] // Cache for 10 minutes
        public async Task<ActionResult<object>> GetContainerInfo(string containerName)
        {
            try
            {
                var allowedContainers = new[] { 
                    _configuration["BlobStorage:ImagesContainer"], 
                    _configuration["BlobStorage:TextFilesContainer"] 
                };
                
                if (!allowedContainers.Contains(containerName))
                {
                    return BadRequest("Invalid container name");
                }

                // This would require additional blob storage service methods
                // For now, return basic info
                var info = new
                {
                    containerName = containerName,
                    isImagesContainer = containerName == _configuration["BlobStorage:ImagesContainer"],
                    maxFileSize = containerName == _configuration["BlobStorage:ImagesContainer"] ? "5MB" : "2MB",
                    allowedTypes = containerName == _configuration["BlobStorage:ImagesContainer"] 
                        ? new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp" }
                        : new[] { "txt", "md", "json", "xml", "csv", "log" }
                };

                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting container info: {containerName}");
                return StatusCode(500, "Internal server error");
            }
        }

        private static bool IsImageFile(IFormFile file)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            
            // Also check content type for additional validation
            var validContentTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp" };
            
            return imageExtensions.Contains(extension) && 
                   (string.IsNullOrEmpty(file.ContentType) || validContentTypes.Contains(file.ContentType));
        }

        private static bool IsTextFile(IFormFile file)
        {
            var textExtensions = new[] { ".txt", ".md", ".json", ".xml", ".csv", ".log" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            
            // Also check content type for additional validation
            var validContentTypes = new[] { "text/plain", "text/markdown", "application/json", "application/xml", "text/csv" };
            
            return textExtensions.Contains(extension) && 
                   (string.IsNullOrEmpty(file.ContentType) || validContentTypes.Contains(file.ContentType));
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