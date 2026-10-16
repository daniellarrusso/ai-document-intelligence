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

    public async Task<Guid> CreateDocumentAsync(Document document, Guid? claimId = null, Guid? linkedByUserId = null, CancellationToken cancellationToken = default)
    {
        _context.Documents.Add(document);
        if (claimId is { } id)
        {
            _context.ClaimDocuments.Add(NewLink(document.Id, id, linkedByUserId));
        }

        // One SaveChanges is one transaction: the document and its link are saved together or not at all.
        await _context.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    public async Task LinkToClaimAsync(Guid documentId, Guid claimId, Guid linkedByUserId, CancellationToken cancellationToken = default)
    {
        if (await _context.ClaimDocuments.AnyAsync(l => l.DocumentId == documentId && l.ClaimId == claimId, cancellationToken))
        {
            return;
        }

        _context.ClaimDocuments.Add(NewLink(documentId, claimId, linkedByUserId));
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkFromClaimAsync(Guid documentId, Guid claimId, CancellationToken cancellationToken = default)
    {
        await _context.ClaimDocuments
            .Where(l => l.DocumentId == documentId && l.ClaimId == claimId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static ClaimDocument NewLink(Guid documentId, Guid claimId, Guid? linkedByUserId) => new()
    {
        DocumentId = documentId,
        ClaimId = claimId,
        LinkedAt = DateTime.UtcNow,
        LinkedByUserId = linkedByUserId
    };

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

    public async Task<List<Document>> GetDocumentsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, string? search = null, bool unmatchedOnly = false, CancellationToken cancellationToken = default)
    {
        var query = VisibleTo(user);

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(d => EF.Functions.ILike(d.FileName, $"%{EscapeLikePattern(search)}%", LikeEscape));
        }

        if (unmatchedOnly)
        {
            query = query.Where(d => !_context.ClaimDocuments.Any(l => l.DocumentId == d.Id));
        }

        return await query
            .OrderByDescending(d => d.UploadedAt)
            .ThenBy(d => d.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    // EF emits ESCAPE '' unless told otherwise, which disables escaping entirely.
    private const string LikeEscape = "\\";

    // Makes the user's text match literally: LIKE treats %, _ and the escape character (\) as special.
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // A document is visible if any claim it is linked to is visible. An unmatched document follows
    // ClaimAccess.CanViewStandaloneDocument. Keep this in step with that method.
    private IQueryable<Document> VisibleTo(CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var documents = _context.Documents.AsNoTracking();
        var userId = user.Id;
        var viewableClaimIds = _context.Claims.Where(ClaimAccess.CanView(user)).Select(c => c.Id);
        var links = _context.ClaimDocuments;

        if (user.IsAdmin)
        {
            return documents;
        }

        if (user.IsHandler)
        {
            return documents.Where(d => links.Any(l => l.DocumentId == d.Id)
                ? links.Any(l => l.DocumentId == d.Id && viewableClaimIds.Contains(l.ClaimId))
                : d.UploadedByUserId == userId);
        }

        if (user.IsReader)
        {
            return documents.Where(d => !links.Any(l => l.DocumentId == d.Id)
                || links.Any(l => l.DocumentId == d.Id && viewableClaimIds.Contains(l.ClaimId)));
        }

        return documents.Where(d => false);
    }
}
