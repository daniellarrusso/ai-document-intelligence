using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Processes queued documents: downloads the stored blob, extracts its text content, then generates a summary.
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

            await repository.UpdateStatusAsync(documentId, DocumentStatus.ExtractingText, null, cancellationToken);

            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            var extractor = scope.ServiceProvider.GetRequiredService<IDocumentTextExtractor>();

            await using var content = await storage.DownloadAsync(document.BlobName, cancellationToken);
            var extractedText = await extractor.ExtractTextAsync(content, document.ContentType, cancellationToken);

            await repository.SaveExtractedTextAsync(documentId, extractedText, cancellationToken);

            await TrySummarizeAsync(scope.ServiceProvider, repository, documentId, extractedText, cancellationToken);

            await repository.MarkCompletedAsync(documentId, DateTime.UtcNow, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);
            await TryMarkFailedAsync(documentId);
        }
    }

    // The DbContext used above may be left in a bad state by a failed SaveChangesAsync (e.g. the change
    // tracker still holds the entity that caused the failure), so recording Failed uses a fresh scope/context
    // rather than reusing it. If even this fails, we log instead of letting it go unhandled and silently
    // leaving the document stuck at its last status, which the UI would never learn is actually a failure.
    private async Task TryMarkFailedAsync(Guid documentId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            await repository.UpdateStatusAsync(documentId, DocumentStatus.Failed, DateTime.UtcNow, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record Failed status for document {DocumentId}; it will remain stuck at its last status", documentId);
        }
    }

    // Summarization is a best-effort enrichment: a failure here (e.g. the local model isn't running)
    // must not undo the successful extraction, so it's isolated from the outer catch that marks Failed.
    private async Task TrySummarizeAsync(
        IServiceProvider services,
        IDocumentRepository repository,
        Guid documentId,
        string extractedText,
        CancellationToken cancellationToken)
    {
        try
        {
            var summarizer = services.GetRequiredService<IDocumentSummarizer>();
            var summary = await summarizer.SummarizeAsync(extractedText, cancellationToken);
            await repository.UpdateSummaryAsync(documentId, summary, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Summarization failed for document {DocumentId}; document remains Completed without a summary", documentId);
        }
    }
}
