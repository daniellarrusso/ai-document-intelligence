using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AiDocumentIntelligence.Infrastructure;

public class DocumentChunkRepository : IDocumentChunkRepository
{
    private readonly AppDbContext _context;

    public DocumentChunkRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        _context.DocumentChunks.AddRange(chunks);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunkMatch>> SearchAsync(Guid documentId, ReadOnlyMemory<float> queryEmbedding, int limit, CancellationToken cancellationToken = default)
    {
        var query = new Vector(queryEmbedding);

        var rows = await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.Embedding.CosineDistance(query))
            .Take(limit)
            .Select(c => new { c.ChunkIndex, c.Text, Distance = c.Embedding.CosineDistance(query) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DocumentChunkMatch(r.ChunkIndex, r.Text, 1 - r.Distance)).ToList();
    }
}
