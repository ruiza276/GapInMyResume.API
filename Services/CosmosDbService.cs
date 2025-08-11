using Microsoft.Azure.Cosmos;
using GapInMyResume.API.Models;
using System.Net;

namespace GapInMyResume.API.Services
{
    public interface ICosmosDbService
    {
        Task<IEnumerable<TimelineItem>> GetTimelineItemsAsync();
        Task<IEnumerable<TimelineItem>> GetTimelineItemsByDateAsync(DateTime date);
        Task<TimelineItem?> GetTimelineItemAsync(string id);
        Task<TimelineItem> CreateTimelineItemAsync(TimelineItem item);
        Task<TimelineItem?> UpdateTimelineItemAsync(string id, TimelineItem item);
        Task<bool> DeleteTimelineItemAsync(string id);
        
        Task<IEnumerable<VisitorMessage>> GetMessagesAsync();
        Task<VisitorMessage> CreateMessageAsync(VisitorMessage message);
        Task<bool> DeleteMessageAsync(string id);
        Task AddMessageAsync(VisitorMessage message);
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

        // Optimized query for all timeline items
        public async Task<IEnumerable<TimelineItem>> GetTimelineItemsAsync()
        {
            try
            {
                // Use efficient query with proper indexing
                var query = "SELECT * FROM c ORDER BY c.date DESC";
                var queryDefinition = new QueryDefinition(query);
                
                var iterator = _timelineContainer.GetItemQueryIterator<TimelineItem>(queryDefinition);
                var results = new List<TimelineItem>();
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                    
                    // Log RU consumption to monitor usage
                    _logger.LogInformation($"Timeline query consumed {response.RequestCharge} RUs");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timeline items from Cosmos DB");
                return new List<TimelineItem>();
            }
        }

        // Optimized query for specific date
        public async Task<IEnumerable<TimelineItem>> GetTimelineItemsByDateAsync(DateTime date)
        {
            try
            {
                // Efficient query using date as filter
                var dateString = date.ToString("yyyy-MM-dd");
                var query = "SELECT * FROM c WHERE STARTSWITH(c.date, @date)";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@date", dateString);
                
                var iterator = _timelineContainer.GetItemQueryIterator<TimelineItem>(queryDefinition);
                var results = new List<TimelineItem>();
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                    
                    _logger.LogInformation($"Date query consumed {response.RequestCharge} RUs");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching timeline items for date {date}");
                throw;
            }
        }

        public async Task<TimelineItem?> GetTimelineItemAsync(string id)
        {
            try
            {
                var response = await _timelineContainer.ReadItemAsync<TimelineItem>(id, new PartitionKey(id));
                _logger.LogInformation($"Single item query consumed {response.RequestCharge} RUs");
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching timeline item with ID: {id}");
                throw;
            }
        }

        public async Task<TimelineItem> CreateTimelineItemAsync(TimelineItem item)
        {
            try
            {
                var response = await _timelineContainer.CreateItemAsync(item, new PartitionKey(item.Id));
                _logger.LogInformation($"Timeline item creation consumed {response.RequestCharge} RUs");
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
                _logger.LogInformation($"Timeline item update consumed {response.RequestCharge} RUs");
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating timeline item with ID: {id}");
                throw;
            }
        }

        public async Task<bool> DeleteTimelineItemAsync(string id)
        {
            try
            {
                var response = await _timelineContainer.DeleteItemAsync<TimelineItem>(id, new PartitionKey(id));
                _logger.LogInformation($"Timeline item deletion consumed {response.RequestCharge} RUs");
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting timeline item with ID: {id}");
                throw;
            }
        }

        // Messages - optimized versions
        public async Task<IEnumerable<VisitorMessage>> GetMessagesAsync()
        {
            try
            {
                var query = "SELECT * FROM c ORDER BY c.timestamp DESC";
                var queryDefinition = new QueryDefinition(query);
                
                var iterator = _messagesContainer.GetItemQueryIterator<VisitorMessage>(queryDefinition);
                var results = new List<VisitorMessage>();
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                    
                    _logger.LogInformation($"Messages query consumed {response.RequestCharge} RUs");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages from Cosmos DB");
                return new List<VisitorMessage>();
            }
        }

        public async Task<VisitorMessage> CreateMessageAsync(VisitorMessage message)
        {
            try
            {
                var response = await _messagesContainer.CreateItemAsync(message, new PartitionKey(message.Id));
                _logger.LogInformation($"Message creation consumed {response.RequestCharge} RUs");
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating message: {message.Id}");
                throw;
            }
        }

        // For backward compatibility
        public async Task AddMessageAsync(VisitorMessage message)
        {
            await CreateMessageAsync(message);
        }

        public async Task<bool> DeleteMessageAsync(string id)
        {
            try
            {
                var response = await _messagesContainer.DeleteItemAsync<VisitorMessage>(id, new PartitionKey(id));
                _logger.LogInformation($"Message deletion consumed {response.RequestCharge} RUs");
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting message with ID: {id}");
                throw;
            }
        }
    }
}