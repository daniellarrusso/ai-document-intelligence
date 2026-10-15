using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public class ClaimService
{
    // numeric(18,2) holds values below 10^16.
    private const decimal MaxAmountClaimed = 9_999_999_999_999_999.99m;

    public const int MaxSearchLength = 100;

    private readonly IClaimRepository _claimRepository;
    private readonly UserService _userService;

    public ClaimService(IClaimRepository claimRepository, UserService userService)
    {
        _claimRepository = claimRepository;
        _userService = userService;
    }

    // Throws ArgumentException if the request is invalid. The reference is generated, new claims start Open,
    // and the creator becomes the assignee so a Handler can see (and work) the claim they just opened.
    public async Task<Claim> CreateClaimAsync(CreateClaimRequest request, CurrentUser creator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(creator);

        var policyNumber = request.PolicyNumber?.Trim();
        if (string.IsNullOrEmpty(policyNumber))
        {
            throw new ArgumentException("Policy number is required.");
        }
        if (policyNumber.Length > Claim.MaxPolicyNumberLength)
        {
            throw new ArgumentException($"Policy number cannot exceed {Claim.MaxPolicyNumberLength} characters.");
        }

        var claimantName = request.ClaimantName?.Trim();
        if (string.IsNullOrEmpty(claimantName))
        {
            throw new ArgumentException("Claimant name is required.");
        }
        if (claimantName.Length > Claim.MaxNameLength)
        {
            throw new ArgumentException($"Claimant name cannot exceed {Claim.MaxNameLength} characters.");
        }

        // The creator is the default assignee. Assigning to someone else needs a known Handler: the picker
        // only offers those, so anything else is a stale or hand-crafted request.
        var assigneeId = creator.Id;
        if (request.AssignedToUserId is { } requestedAssignee && requestedAssignee != creator.Id)
        {
            await RequireHandlerAsync(requestedAssignee, cancellationToken);
            assigneeId = requestedAssignee;
        }

        var now = DateTime.UtcNow;
        if (request.IncidentDate > DateOnly.FromDateTime(now))
        {
            throw new ArgumentException("Incident date cannot be in the future.");
        }

        var amount = Math.Round(request.AmountClaimed, Claim.AmountScale, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new ArgumentException("Amount claimed must be greater than zero.");
        }
        if (amount > MaxAmountClaimed)
        {
            throw new ArgumentException($"Amount claimed cannot exceed {MaxAmountClaimed:0.00}.");
        }

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            Reference = $"CLM-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            PolicyNumber = policyNumber,
            ClaimantName = claimantName,
            IncidentDate = request.IncidentDate,
            AmountClaimed = amount,
            Status = ClaimStatus.Open,
            AssignedToUserId = assigneeId,
            CreatedByUserId = creator.Id,
            IsPrivate = request.IsPrivate,
            CreatedAt = now
        };

        // The foreign keys need the user row to exist, even if they never called /api/me.
        await _userService.RecordSignInAsync(creator, cancellationToken);
        await _claimRepository.CreateClaimAsync(claim, cancellationToken);

        return claim;
    }

    // Throws ArgumentException if the search text is too long. Blank search text means no text filter.
    public Task<List<Claim>> GetClaimsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (trimmedSearch?.Length > MaxSearchLength)
        {
            throw new ArgumentException($"Search text cannot exceed {MaxSearchLength} characters.");
        }

        return _claimRepository.GetClaimsAsync(user, pageNumber, pageSize, trimmedSearch, status, cancellationToken);
    }

    public Task<Claim?> GetClaimWithDocumentsAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        return _claimRepository.GetClaimWithDocumentsAsync(user, id, cancellationToken);
    }

    // NotFound when the claim doesn't exist or the user may not view it; Forbidden when they can view it but not act on it.
    public async Task<AccessOutcome> AuthorizeActionAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await _claimRepository.GetClaimAsync(user, id, cancellationToken);
        if (claim == null)
        {
            return AccessOutcome.NotFound;
        }

        return ClaimAccess.CanAct(user, claim) ? AccessOutcome.Allowed : AccessOutcome.Forbidden;
    }

    // Reassigns the claim to a Handler. NotFound/Forbidden as for AuthorizeActionAsync. Throws ArgumentException
    // if the assignee is not a known Handler. Afterwards the previous Handler can no longer see the claim.
    public async Task<AccessOutcome> AssignClaimAsync(CurrentUser user, Guid claimId, Guid assigneeId, CancellationToken cancellationToken = default)
    {
        var outcome = await AuthorizeActionAsync(user, claimId, cancellationToken);
        if (outcome != AccessOutcome.Allowed)
        {
            return outcome;
        }

        await RequireHandlerAsync(assigneeId, cancellationToken);
        await _claimRepository.UpdateAssigneeAsync(claimId, assigneeId, cancellationToken);

        return AccessOutcome.Allowed;
    }

    public async Task<AccessOutcome> SetPrivacyAsync(CurrentUser user, Guid claimId, bool isPrivate, CancellationToken cancellationToken = default)
    {
        var outcome = await AuthorizeActionAsync(user, claimId, cancellationToken);
        if (outcome != AccessOutcome.Allowed)
        {
            return outcome;
        }

        await _claimRepository.UpdatePrivacyAsync(claimId, isPrivate, cancellationToken);

        return AccessOutcome.Allowed;
    }

    private async Task RequireHandlerAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await _userService.FindHandlerAsync(userId, cancellationToken) == null)
        {
            throw new ArgumentException("The assignee must be a handler.");
        }
    }
}
