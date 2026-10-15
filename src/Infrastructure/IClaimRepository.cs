using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public interface IClaimRepository
{
    Task CreateClaimAsync(Claim claim, CancellationToken cancellationToken = default);

    // Newest first, limited to the claims the user may view (see ClaimAccess). Returned claims do not include
    // their documents. A non-null search is matched case-insensitively as a substring of the reference, policy
    // number or claimant name; a non-null status restricts the result to that status.
    Task<List<Claim>> GetClaimsAsync(CurrentUser user, int pageNumber, int pageSize, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default);

    // Includes the claim's documents (newest first) without their extracted text. Null if the claim does not
    // exist or the user may not view it.
    Task<Claim?> GetClaimWithDocumentsAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default);

    // Sets the assignee. No-op if the claim does not exist. Callers must have authorised the change.
    Task UpdateAssigneeAsync(Guid claimId, Guid assigneeId, CancellationToken cancellationToken = default);

    // Sets whether the claim is hidden from Readers. No-op if the claim does not exist.
    Task UpdatePrivacyAsync(Guid claimId, bool isPrivate, CancellationToken cancellationToken = default);

    // The claim without its documents. Null if it does not exist or the user may not view it.
    Task<Claim?> GetClaimAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default);
}
