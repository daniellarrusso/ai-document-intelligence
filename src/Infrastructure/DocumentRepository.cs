using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using Microsoft.EntityFrameworkCore;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _context;

    public DocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> CreateDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FindAsync(new object[] { id }, cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateStatusAsync(Guid id, DocumentStatus status, DateTime? processedAt = null, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return;
        }

        document.Status = status;
        document.ProcessedAt = processedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveExtractedTextAsync(Guid id, string extractedText, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return;
        }

        document.Status = DocumentStatus.GeneratingSummary;
        document.ExtractedText = extractedText;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(Guid id, DateTime processedAt, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return;
        }

        document.Status = DocumentStatus.Completed;
        document.ProcessedAt = processedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSummaryAsync(Guid id, string summary, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document == null)
        {
            return;
        }

        document.Summary = summary;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return document;
    }

    public async Task<Document?> GetDocumentForUserAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        return await VisibleTo(user).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<Document>> GetDocumentsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        return await VisibleTo(user)
            .OrderByDescending(d => d.UploadedAt)
            .ThenBy(d => d.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    // A document inherits its claim's visibility; standalone documents follow ClaimAccess.CanViewStandaloneDocument.
    // Keep this in step with that method.
    private IQueryable<Document> VisibleTo(CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var documents = _context.Documents.AsNoTracking();
        var userId = user.Id;
        var viewableClaimIds = _context.Claims.Where(ClaimAccess.CanView(user)).Select(c => c.Id);

        if (user.IsAdmin)
        {
            return documents;
        }

        if (user.IsHandler)
        {
            return documents.Where(d => d.ClaimId == null
                ? d.UploadedByUserId == userId
                : viewableClaimIds.Contains(d.ClaimId.Value));
        }

        if (user.IsReader)
        {
            return documents.Where(d => d.ClaimId == null || viewableClaimIds.Contains(d.ClaimId.Value));
        }

        return documents.Where(d => false);
    }
}
