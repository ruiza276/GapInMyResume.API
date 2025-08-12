using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.ResponseCaching;
using GapInMyResume.API.Services;
using GapInMyResume.API.Middleware;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add response caching for optimization
        builder.Services.AddResponseCaching(options =>
        {
            options.MaximumBodySize = 1024 * 1024; // 1MB
            options.UseCaseSensitivePaths = false;
        });

        // Add response compression for production
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
        });

        // Add memory cache for optimization
        builder.Services.AddMemoryCache();

        // Add usage monitoring service
        builder.Services.AddSingleton<UsageMonitoringService>();

        // Configure CORS - Enhanced for production
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowReactApp", policy =>
            {
                policy.WithOrigins(
                        "http://localhost:3000", 
                        "https://localhost:3000",
                        "https://*.netlify.app",
                        "https://gapinmyresume.dev"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // Configure Azure Blob Storage with error handling
        try
        {
            builder.Services.AddSingleton(x =>
            {
                var connectionString = builder.Configuration["BlobStorage__ConnectionString"] 
                    ?? builder.Configuration["BlobStorage:ConnectionString"]
                    ?? builder.Configuration.GetConnectionString("BlobStorage");

                if (string.IsNullOrEmpty(connectionString))
                {
                    // Log the issue but don't fail startup
                    Console.WriteLine("WARNING: Blob Storage connection string not found");
                    return null;
                }

                return new BlobServiceClient(connectionString);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Failed to configure Blob Storage: {ex.Message}");
        }

        // Configure Azure Cosmos DB with error handling
        try
        {
            builder.Services.AddSingleton(x =>
            {
                var connectionString = builder.Configuration["CosmosDb__ConnectionString"] 
                    ?? builder.Configuration["CosmosDb:ConnectionString"]
                    ?? builder.Configuration.GetConnectionString("CosmosDb");

                if (string.IsNullOrEmpty(connectionString))
                {
                    // Log the issue but don't fail startup
                    Console.WriteLine("WARNING: Cosmos DB connection string not found");
                    return null;
                }

                var options = new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    },
                    // Production optimizations but more lenient timeouts
                    ConnectionMode = ConnectionMode.Direct,
                    RequestTimeout = TimeSpan.FromSeconds(60), // Increased timeout
                    OpenTcpConnectionTimeout = TimeSpan.FromSeconds(20), // Increased timeout
                    IdleTcpConnectionTimeout = TimeSpan.FromMinutes(10)
                };
                return new CosmosClient(connectionString, options);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Failed to configure Cosmos DB: {ex.Message}");
        }

        // Register services with null checks
        builder.Services.AddScoped<IBlobStorageService>(provider =>
        {
            var blobClient = provider.GetService<BlobServiceClient>();
            var logger = provider.GetRequiredService<ILogger<BlobStorageService>>();
            return new BlobStorageService(blobClient, logger);
        });

        builder.Services.AddScoped<ICosmosDbService>(provider =>
        {
            var cosmosClient = provider.GetService<CosmosClient>();
            var config = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetRequiredService<ILogger<CosmosDbService>>();
            return new CosmosDbService(cosmosClient, config, logger);
        });

        var app = builder.Build();

        // Log startup information
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Application starting up...");
        logger.LogInformation($"Environment: {app.Environment.EnvironmentName}");

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            // Production-only middleware
            app.UseHttpsRedirection();
            app.UseHsts(); // HTTP Strict Transport Security
        }

        // Use response compression (for production)
        app.UseResponseCompression();

        // Configure caching middleware
        app.UseResponseCaching();

        // Add cache headers for API responses
        app.Use(async (context, next) =>
        {
            try
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    // API responses - different cache times for different endpoints
                    if (context.Request.Path.StartsWithSegments("/api/timeline"))
                    {
                        context.Response.GetTypedHeaders().CacheControl =
                            new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                            {
                                Public = true,
                                MaxAge = TimeSpan.FromMinutes(5)
                            };
                    }
                    else if (context.Request.Path.StartsWithSegments("/api/messages"))
                    {
                        context.Response.GetTypedHeaders().CacheControl =
                            new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                            {
                                Public = true,
                                MaxAge = TimeSpan.FromMinutes(2)
                            };
                    }
                }
                
                await next();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in cache middleware");
                await next();
            }
        });

        // Add usage tracking middleware with error handling
        try
        {
            app.UseMiddleware<UsageTrackingMiddleware>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add usage tracking middleware");
        }

        // Use CORS
        app.UseCors("AllowReactApp");

        app.UseAuthorization();

        app.MapControllers();

        // Simple health check that always works
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            environment = app.Environment.EnvironmentName
        }));

        // Test endpoint
        app.MapGet("/", () => Results.Ok(new
        {
            message = "GapInMyResume API is running",
            timestamp = DateTime.UtcNow
        }));

        // Initialize usage monitoring with error handling
        try
        {
            var serviceProvider = app.Services;
            using (var scope = serviceProvider.CreateScope())
            {
                var usageMonitor = scope.ServiceProvider.GetRequiredService<UsageMonitoringService>();
                usageMonitor.LogStartupInfo();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize usage monitoring");
        }

        logger.LogInformation("Application startup complete. Starting web host...");
        
        app.Run();
    }
}