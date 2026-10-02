using Pgvector;

namespace AiDocumentIntelligence.Infrastructure;

// Persistence-only entity: lives here rather than in Domain so the Pgvector types stay out of Domain.
public class DocumentChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int ChunkIndex { get; set; }

    public string Text { get; set; } = null!;

    public Vector Embedding { get; set; } = null!;
}
