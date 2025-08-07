using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using GapInMyResume.API.Services;

public class Program
{
    public static void Main(string[] args)
    {

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Configure CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowReactApp", policy =>
            {
                policy.WithOrigins(
                        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000" }
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

        app.UseCors("AllowReactApp");

        app.UseAuthorization();

        app.MapControllers();

        app.Run();

    }
}