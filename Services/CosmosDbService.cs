using Microsoft.Azure.Cosmos;
using GapInMyResume.API.Models;
using System.Net;

namespace GapInMyResume.API.Services
{
    public interface ICosmosDbService
    {
        Task<IEnumerable<TimelineItem>> GetTimelineItemsAsync();
        Task<TimelineItem?> GetTimelineItemAsync(string id);
        Task<TimelineItem> CreateTimelineItemAsync(TimelineItem item);
        Task<TimelineItem?> UpdateTimelineItemAsync(string id, TimelineItem item);
        Task<bool> DeleteTimelineItemAsync(string id);
        
        Task<IEnumerable<VisitorMessage>> GetMessagesAsync();
        Task<VisitorMessage> CreateMessageAsync(VisitorMessage message);
        Task<bool> DeleteMessageAsync(string id);
    }

    public class CosmosDbService : ICosmosDbService
    {
        private readonly Container _timelineContainer;
        private readonly Container _messagesContainer;
        private readonly ILogger<CosmosDbService> _logger;

        public CosmosDbService(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosDbService> logger)
        {
            var databaseName = configuration["CosmosDb:DatabaseName"];
            var timelineContainerName = configuration["CosmosDb:TimelineContainer"];
            var messagesContainerName = configuration["CosmosDb:MessagesContainer"];

            _timelineContainer = cosmosClient.GetContainer(databaseName, timelineContainerName);
            _messagesContainer = cosmosClient.GetContainer(databaseName, messagesContainerName);
            _logger = logger;
        }

        // Timeline Items
        public async Task<IEnumerable<TimelineItem>> GetTimelineItemsAsync()
        {
            try
            {
                var query = _timelineContainer.GetItemQueryIterator<TimelineItem>(
                    "SELECT * FROM c ORDER BY c.Date DESC");
                
                var results = new List<TimelineItem>();
                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    results.AddRange(response);
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timeline items");
                return new List<TimelineItem>();
            }
        }

        public async Task<TimelineItem?> GetTimelineItemAsync(string id)
        {
            try
            {
                var response = await _timelineContainer.ReadItemAsync<TimelineItem>(id, new PartitionKey(id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<TimelineItem> CreateTimelineItemAsync(TimelineItem item)
        {
            try
            {
                var response = await _timelineContainer.CreateItemAsync(item, new PartitionKey(item.Id));
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating timeline item: {item.Id}");
                throw;
            }
        }

        public async Task<TimelineItem?> UpdateTimelineItemAsync(string id, TimelineItem item)
        {
            try
            {
                item.Id = id; // Ensure ID matches
                var response = await _timelineContainer.UpsertItemAsync(item, new PartitionKey(id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> DeleteTimelineItemAsync(string id)
        {
            try
            {
                await _timelineContainer.DeleteItemAsync<TimelineItem>(id, new PartitionKey(id));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        // Messages
        public async Task<IEnumerable<VisitorMessage>> GetMessagesAsync()
        {
            try
            {
                var query = _messagesContainer.GetItemQueryIterator<VisitorMessage>(
                    "SELECT * FROM c ORDER BY c.Timestamp DESC");
                
                var results = new List<VisitorMessage>();
                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    results.AddRange(response);
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages");
                return new List<VisitorMessage>();
            }
        }

        public async Task<VisitorMessage> CreateMessageAsync(VisitorMessage message)
        {
            try
            {
                var response = await _messagesContainer.CreateItemAsync(message, new PartitionKey(message.Id));
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating message: {message.Id}");
                throw;
            }
        }

        public async Task<bool> DeleteMessageAsync(string id)
        {
            try
            {
                await _messagesContainer.DeleteItemAsync<VisitorMessage>(id, new PartitionKey(id));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}