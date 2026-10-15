using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public interface IClaimRepository
{
    Task CreateClaimAsync(Claim claim, CancellationToken cancellationToken = default);

    // Newest first. Returned claims do not include their documents. A non-null search is matched
    // case-insensitively as a substring of the reference, policy number or claimant name; a non-null
    // status restricts the result to that status.
    Task<List<Claim>> GetClaimsAsync(int pageNumber, int pageSize, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default);

    // Includes the claim's documents (newest first) without their extracted text. Null if the claim does not exist.
    Task<Claim?> GetClaimWithDocumentsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ClaimExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
