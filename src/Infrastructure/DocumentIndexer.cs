using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.AI;

namespace AiDocumentIntelligence.Infrastructure;

public class DocumentIndexer : IDocumentIndexer
{
    // Bounds the size of each embedding request so a long document doesn't become one huge call.
    private const int EmbeddingBatchSize = 32;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IDocumentChunkRepository _chunkRepository;

    public DocumentIndexer(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, IDocumentChunkRepository chunkRepository)
    {
        _embeddingGenerator = embeddingGenerator;
        _chunkRepository = chunkRepository;
    }

    public async Task IndexAsync(Guid documentId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Document text cannot be null or empty.", nameof(text));
        }

        var texts = TextChunker.Split(text);
        var chunks = new List<DocumentChunk>(texts.Count);

        try
        {
            for (var offset = 0; offset < texts.Count; offset += EmbeddingBatchSize)
            {
                var batch = texts.Skip(offset).Take(EmbeddingBatchSize).ToList();
                var embeddings = await _embeddingGenerator.GenerateAsync(batch, cancellationToken: cancellationToken);

                if (embeddings.Count != batch.Count)
                {
                    throw new InvalidOperationException($"Embedding model returned {embeddings.Count} embeddings for {batch.Count} chunks.");
                }

                for (var i = 0; i < batch.Count; i++)
                {
                    var vector = embeddings[i].Vector;
                    if (vector.Length != EmbeddingDefaults.Dimensions)
                    {
                        throw new InvalidOperationException(
                            $"Embedding model returned {vector.Length} dimensions but the index expects {EmbeddingDefaults.Dimensions}.");
                    }

                    chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        ChunkIndex = offset + i,
                        Text = batch[i],
                        Embedding = new Pgvector.Vector(vector),
                    });
                }
            }

            await _chunkRepository.ReplaceChunksAsync(documentId, chunks, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to index the document.", ex);
        }
    }
}
