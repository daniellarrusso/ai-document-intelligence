using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Processes queued documents: downloads the stored blob and extracts its text content.
public class LocalDocumentProcessingWorker : BackgroundService
{
    private readonly LocalDocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDocumentProcessingWorker> _logger;

    public LocalDocumentProcessingWorker(
        LocalDocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDocumentProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var documentId in _queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(documentId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad document must not stop the worker (and, by default, the whole host).
                    _logger.LogError(ex, "Unhandled error processing document {DocumentId}", documentId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: the channel read (or an in-flight document) was cancelled.
        }
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        try
        {
            var document = await repository.GetDocumentByIdAsync(documentId, cancellationToken);
            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} no longer exists; skipping processing", documentId);
                return;
            }

            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            var extractor = scope.ServiceProvider.GetRequiredService<IDocumentTextExtractor>();

            await using var content = await storage.DownloadAsync(document.BlobName, cancellationToken);
            var extractedText = await extractor.ExtractTextAsync(content, document.ContentType, cancellationToken);

            await repository.CompleteProcessingAsync(documentId, extractedText, DateTime.UtcNow, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);
            await repository.UpdateStatusAsync(documentId, DocumentStatus.Failed, DateTime.UtcNow, CancellationToken.None);
        }
    }
}
