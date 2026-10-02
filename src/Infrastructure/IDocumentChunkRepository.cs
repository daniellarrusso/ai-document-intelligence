using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public interface IDocumentChunkRepository
{
    // Replaces all chunks stored for the document with the supplied ones.
    Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    // Returns the document's chunks most similar to the query embedding, most relevant first.
    Task<IReadOnlyList<DocumentChunkMatch>> SearchAsync(Guid documentId, ReadOnlyMemory<float> queryEmbedding, int limit, CancellationToken cancellationToken = default);
}
