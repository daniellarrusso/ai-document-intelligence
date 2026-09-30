using AiDocumentIntelligence.Infrastructure;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddScoped<IDocumentStorage, BlobDocumentStorage>();
builder.Services.AddScoped<IDocumentTextExtractor, PdfDocumentTextExtractor>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IDocumentSummarizer, OllamaDocumentSummarizer>();
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return new OllamaApiClient(new Uri(options.BaseUrl), options.Model);
});
builder.Services.AddScoped<DocumentService>();
builder.Services.AddSingleton<LocalDocumentProcessingQueue>();
builder.Services.AddSingleton<IDocumentProcessingQueue>(sp => sp.GetRequiredService<LocalDocumentProcessingQueue>());
builder.Services.AddHostedService<LocalDocumentProcessingWorker>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<AzureStorageOptions>(
    builder.Configuration.GetSection("AzureStorage"));

builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;

    return new BlobServiceClient(options.ConnectionString);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("AllowAngular");
app.UseAuthorization();

app.MapControllers();

app.Run();
