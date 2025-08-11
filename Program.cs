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

        // Add memory cache for optimization
        builder.Services.AddMemoryCache();

        // Add usage monitoring service
        builder.Services.AddSingleton<UsageMonitoringService>();

        // Configure CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowReactApp", policy =>
            {
                policy.WithOrigins(
                        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000", "https://localhost:3000" }
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // Configure Azure Blob Storage
        builder.Services.AddSingleton(x =>
        {
            var connectionString = builder.Configuration["BlobStorage:ConnectionString"];
            return new BlobServiceClient(connectionString);
        });

        // Configure Azure Cosmos DB
        builder.Services.AddSingleton(x =>
        {
            var connectionString = builder.Configuration["CosmosDb:ConnectionString"];
            var options = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
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

        app.UseHttpsRedirection();

        // Configure caching middleware
        app.UseResponseCaching();

        // Add cache headers for API responses
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // API responses - short cache for frequently accessed endpoints
                if (context.Request.Path.StartsWithSegments("/api/timeline"))
                {
                    context.Response.GetTypedHeaders().CacheControl =
                        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                        {
                            Public = true,
                            MaxAge = TimeSpan.FromMinutes(5)
                        };
                }
            }
            
            await next();
        });

        // Add usage tracking middleware
        app.UseMiddleware<UsageTrackingMiddleware>();

        app.UseCors("AllowReactApp");

        app.UseAuthorization();

        app.MapControllers();

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