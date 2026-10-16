using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiDocumentIntelligence.Infrastructure;

public class ClaimRepository : IClaimRepository
{
    // EF emits ESCAPE '' unless told otherwise, which disables escaping entirely.
    private const string LikeEscape = "\\";

    private readonly AppDbContext _context;

    public ClaimRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateClaimAsync(Claim claim, CancellationToken cancellationToken = default)
    {
        _context.Claims.Add(claim);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Claim>> GetClaimsAsync(CurrentUser user, int pageNumber, int pageSize, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Claims.AsNoTracking().Include(c => c.AssignedToUser).Where(ClaimAccess.CanView(user));

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLikePattern(search)}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Reference, pattern, LikeEscape) ||
                EF.Functions.ILike(c.PolicyNumber, pattern, LikeEscape) ||
                EF.Functions.ILike(c.ClaimantName, pattern, LikeEscape));
        }

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    // Makes the user's text match literally: LIKE treats %, _ and the escape character (\) as special.
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<Claim?> GetClaimWithDocumentsAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        // Projected rather than Include()d so each document's potentially large ExtractedText isn't loaded.
        return await _context.Claims
            .AsNoTracking()
            .Where(ClaimAccess.CanView(user))
            .Where(c => c.Id == id)
            .Select(c => new Claim
            {
                Id = c.Id,
                Reference = c.Reference,
                PolicyNumber = c.PolicyNumber,
                ClaimantName = c.ClaimantName,
                IncidentDate = c.IncidentDate,
                AmountClaimed = c.AmountClaimed,
                Status = c.Status,
                AssignedToUserId = c.AssignedToUserId,
                AssignedToUser = c.AssignedToUser == null
                    ? null
                    : new AppUser { Id = c.AssignedToUser.Id, DisplayName = c.AssignedToUser.DisplayName },
                CreatedByUserId = c.CreatedByUserId,
                IsPrivate = c.IsPrivate,
                CreatedAt = c.CreatedAt,
                Documents = c.Documents
                    .OrderByDescending(d => d.UploadedAt)
                    .Select(d => new Document
                    {
                        Id = d.Id,
                        FileName = d.FileName,
                        ContentType = d.ContentType,
                        FileSize = d.FileSize,
                        Status = d.Status,
                        UploadedAt = d.UploadedAt,
                        ProcessedAt = d.ProcessedAt,
                        Summary = d.Summary
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Claim?> GetClaimAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Claims
            .AsNoTracking()
            .Where(ClaimAccess.CanView(user))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task UpdateAssigneeAsync(Guid claimId, Guid assigneeId, CancellationToken cancellationToken = default)
    {
        await _context.Claims
            .Where(c => c.Id == claimId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AssignedToUserId, assigneeId), cancellationToken);
    }

    public async Task UpdatePrivacyAsync(Guid claimId, bool isPrivate, CancellationToken cancellationToken = default)
    {
        await _context.Claims
            .Where(c => c.Id == claimId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPrivate, isPrivate), cancellationToken);
    }

    public async Task<List<Claim>> GetClaimsForDocumentAsync(CurrentUser user, Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _context.Claims
            .AsNoTracking()
            .Include(c => c.AssignedToUser)
            .Where(ClaimAccess.CanView(user))
            .Where(c => _context.ClaimDocuments.Any(l => l.ClaimId == c.Id && l.DocumentId == documentId))
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Claim>> GetAllClaimsForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _context.Claims
            .AsNoTracking()
            .Where(c => _context.ClaimDocuments.Any(l => l.ClaimId == c.Id && l.DocumentId == documentId))
            .ToListAsync(cancellationToken);
    }
}
