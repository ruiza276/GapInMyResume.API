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
                // Get allowed origins from configuration with fallbacks
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                    ?? new[] {
                        "http://localhost:3000",
                        "https://localhost:3000",
                        "https://gapinmyresume.dev",
                        "https://gapinmyresume.netlify.app/"
                    };

                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // Configure Azure Blob Storage - Enhanced with multiple connection string sources
        builder.Services.AddSingleton(x =>
        {
            var connectionString = builder.Configuration["BlobStorage:ConnectionString"] 
                ?? builder.Configuration.GetConnectionString("BlobStorage")
                ?? builder.Configuration["BlobStorage__ConnectionString"]; // Azure App Service format

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Blob Storage connection string is not configured");
            }

            return new BlobServiceClient(connectionString);
        });

        // Configure Azure Cosmos DB - Enhanced with multiple connection string sources
        builder.Services.AddSingleton(x =>
        {
            var connectionString = builder.Configuration["CosmosDb:ConnectionString"] 
                ?? builder.Configuration.GetConnectionString("CosmosDb")
                ?? builder.Configuration["CosmosDb__ConnectionString"]; // Azure App Service format

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Cosmos DB connection string is not configured");
            }

            var options = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                // Production optimizations
                ConnectionMode = ConnectionMode.Direct,
                RequestTimeout = TimeSpan.FromSeconds(30),
                OpenTcpConnectionTimeout = TimeSpan.FromSeconds(10),
                IdleTcpConnectionTimeout = TimeSpan.FromMinutes(10)
            };
            return new CosmosClient(connectionString, options);
        });

        // Register services
        builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
        builder.Services.AddScoped<ICosmosDbService, CosmosDbService>();

        var app = builder.Build();

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

        // Add cache headers for API responses - Enhanced
        app.Use(async (context, next) =>
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
                else if (context.Request.Path.StartsWithSegments("/api/files"))
                {
                    context.Response.GetTypedHeaders().CacheControl =
                        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                        {
                            Public = true,
                            MaxAge = TimeSpan.FromHours(1) // Files can be cached longer
                        };
                }
            }
            
            await next();
        });

        // Add usage tracking middleware
        app.UseMiddleware<UsageTrackingMiddleware>();

        // Use CORS
        app.UseCors("AllowReactApp");

        app.UseAuthorization();

        app.MapControllers();

        // Health check endpoint - Enhanced
        app.MapGet("/health", async (IServiceProvider serviceProvider) =>
        {
            try
            {
                // Basic health check with dependency validation
                var cosmosClient = serviceProvider.GetService<CosmosClient>();
                var blobClient = serviceProvider.GetService<BlobServiceClient>();
                
                var health = new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    environment = app.Environment.EnvironmentName,
                    dependencies = new
                    {
                        cosmosDb = cosmosClient != null ? "configured" : "missing",
                        blobStorage = blobClient != null ? "configured" : "missing"
                    }
                };

                return Results.Ok(health);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Health check failed",
                    detail: ex.Message,
                    statusCode: 500
                );
            }
        });

        // API root endpoint for testing
        app.MapGet("/api", () => Results.Ok(new 
        { 
            message = "GapInMyResume API is running",
            version = "1.0.0",
            timestamp = DateTime.UtcNow
        }));

        // Initialize usage monitoring
        var serviceProvider = app.Services;
        using (var scope = serviceProvider.CreateScope())
        {
            var usageMonitor = scope.ServiceProvider.GetRequiredService<UsageMonitoringService>();
            usageMonitor.LogStartupInfo();
        }

        app.Run();
    }
}