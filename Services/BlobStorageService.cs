using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace GapInMyResume.API.Services
{
    public interface IBlobStorageService
    {
        Task<string> UploadFileAsync(IFormFile file, string containerName);
        Task<bool> DeleteFileAsync(string fileName, string containerName);
        Task<Stream> DownloadFileAsync(string fileName, string containerName);
    }

    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<BlobStorageService> _logger;

        public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<string> UploadFileAsync(IFormFile file, string containerName)
        {
            try
            {
                // Ensure container exists with appropriate access level
                var containerClient = await EnsureContainerExistsAsync(containerName);
                
                // Create unique filename
                var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                var blobClient = containerClient.GetBlobClient(fileName);

                // Set content type and cache control
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType ?? GetContentTypeFromExtension(file.FileName),
                    CacheControl = "public, max-age=31536000" // Cache for 1 year
                };

                // Upload file
                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders
                });

                _logger.LogInformation($"File uploaded successfully: {fileName} to container: {containerName}");
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file.FileName} to container: {containerName}");
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileName, string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(fileName);
                
                var response = await blobClient.DeleteIfExistsAsync();
                _logger.LogInformation($"File deletion result: {response.Value} for file: {fileName}");
                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileName} from container: {containerName}");
                return false;
            }
        }

        public async Task<Stream> DownloadFileAsync(string fileName, string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(fileName);
                
                var response = await blobClient.DownloadStreamingAsync();
                _logger.LogInformation($"File downloaded successfully: {fileName} from container: {containerName}");
                return response.Value.Content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName} from container: {containerName}");
                throw;
            }
        }

        /// <summary>
        /// Ensures the container exists with appropriate access level
        /// </summary>
        private async Task<BlobContainerClient> EnsureContainerExistsAsync(string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Determine access level based on container type
                var accessLevel = containerName.ToLower().Contains("image") 
                    ? PublicAccessType.Blob  // Images should be publicly accessible
                    : PublicAccessType.None; // Text files stay private
                
                // Check if container exists, create if it doesn't
                var response = await containerClient.CreateIfNotExistsAsync(accessLevel);
                
                if (response != null)
                {
                    _logger.LogInformation($"Created new blob container: {containerName} with access level: {accessLevel}");
                }
                else
                {
                    // Container exists, but let's make sure images container has public access
                    if (containerName.ToLower().Contains("image"))
                    {
                        try
                        {
                            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);
                            _logger.LogInformation($"Updated access policy for images container: {containerName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Could not update access policy for container: {containerName}");
                        }
                    }
                    _logger.LogDebug($"Container already exists: {containerName}");
                }

                // Set CORS rules for the storage account
                await SetCorsRulesAsync();

                return containerClient;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error ensuring container exists: {containerName}");
                throw;
            }
        }

        /// <summary>
        /// Sets CORS rules for the blob storage account
        /// </summary>
        private async Task SetCorsRulesAsync()
        {
            try
            {
                var serviceProperties = await _blobServiceClient.GetPropertiesAsync();
                
                // Check if CORS rules already exist
                if (serviceProperties.Value.Cors == null || !serviceProperties.Value.Cors.Any())
                {
                    serviceProperties.Value.Cors = new[]
                    {
                        new BlobCorsRule
                        {
                            AllowedOrigins = "*", // In production, replace with your domain
                            AllowedMethods = "GET,PUT,POST,DELETE,HEAD,OPTIONS",
                            AllowedHeaders = "*",
                            ExposedHeaders = "*",
                            MaxAgeInSeconds = 86400 // 24 hours
                        }
                    };
                    
                    await _blobServiceClient.SetPropertiesAsync(serviceProperties.Value);
                    _logger.LogInformation("CORS rules set for blob storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set CORS rules - images may not display properly");
            }
        }

        /// <summary>
        /// Gets content type from file extension
        /// </summary>
        private static string GetContentTypeFromExtension(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}