using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public class ClaimService
{
    // numeric(18,2) holds values below 10^16.
    private const decimal MaxAmountClaimed = 9_999_999_999_999_999.99m;

    public const int MaxSearchLength = 100;

    private readonly IClaimRepository _claimRepository;

    public ClaimService(IClaimRepository claimRepository)
    {
        _claimRepository = claimRepository;
    }

    // Throws ArgumentException if the request is invalid. The reference is generated, new claims start Open.
    public async Task<Claim> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var assignedTo = string.IsNullOrWhiteSpace(request.AssignedTo) ? null : request.AssignedTo.Trim();
        if (assignedTo?.Length > Claim.MaxNameLength)
        {
            throw new ArgumentException($"Assigned to cannot exceed {Claim.MaxNameLength} characters.");
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
            AssignedTo = assignedTo,
            CreatedAt = now
        };

        await _claimRepository.CreateClaimAsync(claim, cancellationToken);

        return claim;
    }

    // Throws ArgumentException if the search text is too long. Blank search text means no text filter.
    public Task<List<Claim>> GetClaimsAsync(int pageNumber = 1, int pageSize = 10, string? search = null, ClaimStatus? status = null, CancellationToken cancellationToken = default)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (trimmedSearch?.Length > MaxSearchLength)
        {
            throw new ArgumentException($"Search text cannot exceed {MaxSearchLength} characters.");
        }

        return _claimRepository.GetClaimsAsync(pageNumber, pageSize, trimmedSearch, status, cancellationToken);
    }

    public Task<Claim?> GetClaimWithDocumentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _claimRepository.GetClaimWithDocumentsAsync(id, cancellationToken);
    }

    public Task<bool> ClaimExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _claimRepository.ClaimExistsAsync(id, cancellationToken);
    }
}
