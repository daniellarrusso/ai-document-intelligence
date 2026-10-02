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

    public async Task<List<Claim>> GetClaimsAsync(int pageNumber, int pageSize, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Claims.AsNoTracking();

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

    public async Task<Claim?> GetClaimWithDocumentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Projected rather than Include()d so each document's potentially large ExtractedText isn't loaded.
        return await _context.Claims
            .AsNoTracking()
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
                AssignedTo = c.AssignedTo,
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
                        Summary = d.Summary,
                        ClaimId = d.ClaimId
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ClaimExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Claims.AnyAsync(c => c.Id == id, cancellationToken);
    }
}
