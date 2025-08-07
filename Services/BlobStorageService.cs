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
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Create unique filename
                var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                var blobClient = containerClient.GetBlobClient(fileName);

                // Set content type
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType
                };

                // Upload file
                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders
                });

                _logger.LogInformation($"File uploaded successfully: {fileName}");
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file.FileName}");
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
                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileName}");
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
                return response.Value.Content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file: {fileName}");
                throw;
            }
        }
    }
}