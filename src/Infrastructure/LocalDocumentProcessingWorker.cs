using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Simulates AI processing: waits, then marks the document Completed (or Failed on error).
public class LocalDocumentProcessingWorker : BackgroundService
{
    private readonly LocalDocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDocumentProcessingWorker> _logger;
    private readonly TimeSpan _processingDelay;

    public LocalDocumentProcessingWorker(
        LocalDocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDocumentProcessingWorker> logger,
        TimeSpan? processingDelay = null)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _processingDelay = processingDelay ?? TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var documentId in _queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(documentId, stoppingToken);
        }
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        try
        {
            await Task.Delay(_processingDelay, cancellationToken);
            await repository.UpdateStatusAsync(documentId, DocumentStatus.Completed, DateTime.UtcNow, cancellationToken);
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
