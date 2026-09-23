using System.Threading.Channels;

// In-process queue for local development. Replace with a durable queue (e.g. Azure Storage Queue / Service Bus) later.
public class LocalDocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(documentId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
