public interface IDocumentProcessingQueue
{
    // Queues a stored document for processing.
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}
