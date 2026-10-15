using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using AiDocumentIntelligence.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.CanRead)]
public class ClaimsController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly ClaimService _claimService;
    private readonly DocumentService _documentService;

    public ClaimsController(ClaimService claimService, DocumentService documentService)
    {
        _claimService = claimService;
        _documentService = documentService;
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPost(Name = "CreateClaim")]
    public async Task<IActionResult> Create([FromBody] CreateClaimRequest request, CancellationToken cancellationToken)
    {
        // A token without an object id can't be recorded as the claim's owner, so fail closed.
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        try
        {
            var claim = await _claimService.CreateClaimAsync(request, user, cancellationToken);

            return CreatedAtRoute("GetClaimById", new { id = claim.Id }, ClaimResponse.From(claim));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet(Name = "GetClaims")]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? q = null,
        [FromQuery] ClaimStatus? status = null)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        if (pageNumber < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest($"pageNumber must be at least 1 and pageSize must be between 1 and {MaxPageSize}.");
        }

        // The repository computes the offset as an int, so refuse pages that would overflow it.
        if ((long)(pageNumber - 1) * pageSize > int.MaxValue)
        {
            return BadRequest("pageNumber is too large for the given pageSize.");
        }

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            return BadRequest("status is not a valid claim status.");
        }

        try
        {
            var claims = await _claimService.GetClaimsAsync(user, pageNumber, pageSize, q, status, cancellationToken);

            return Ok(claims.Select(ClaimResponse.From));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id}", Name = "GetClaimById")]
    public async Task<IActionResult> GetClaimById(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        var claim = await _claimService.GetClaimWithDocumentsAsync(user, id, cancellationToken);

        return claim == null ? NotFound() : Ok(ClaimDetailResponse.From(claim));
    }

    // Hands the claim to another handler. 204 rather than the claim: the previous handler can no longer view it.
    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPut("{id}/assignee", Name = "AssignClaim")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignClaimRequest request, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        if (request.UserId == Guid.Empty)
        {
            return BadRequest("userId is required.");
        }

        try
        {
            return await _claimService.AssignClaimAsync(user, id, request.UserId, cancellationToken) switch
            {
                AccessOutcome.Allowed => NoContent(),
                AccessOutcome.Forbidden => Forbid(),
                _ => NotFound()
            };
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPut("{id}/privacy", Name = "SetClaimPrivacy")]
    public async Task<IActionResult> SetPrivacy(Guid id, [FromBody] SetClaimPrivacyRequest request, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        return await _claimService.SetPrivacyAsync(user, id, request.IsPrivate, cancellationToken) switch
        {
            AccessOutcome.Allowed => NoContent(),
            AccessOutcome.Forbidden => Forbid(),
            _ => NotFound()
        };
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPost("{id}/documents", Name = "UploadClaimDocument")]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be null or empty.");
        }

        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        // 404 when the claim is missing or not visible to the caller (so its existence isn't revealed), 403
        // when they can see it but may not act on it.
        switch (await _claimService.AuthorizeActionAsync(user, id, cancellationToken))
        {
            case AccessOutcome.NotFound:
                return NotFound();
            case AccessOutcome.Forbidden:
                return Forbid();
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.SaveDocumentAsync(new UploadDocumentRequest(
            stream,
            file.FileName,
            file.ContentType,
            file.Length), user, id, cancellationToken);

        return Ok(ClaimDocumentResponse.From(document));
    }
}

public record AssignClaimRequest(Guid UserId);

public record SetClaimPrivacyRequest(bool IsPrivate);

public record ClaimResponse(
    Guid Id,
    string Reference,
    string PolicyNumber,
    string ClaimantName,
    DateOnly IncidentDate,
    decimal AmountClaimed,
    ClaimStatus Status,
    Guid? AssignedToUserId,
    string? AssignedToName,
    bool IsPrivate,
    DateTime CreatedAt)
{
    public static ClaimResponse From(Claim claim) => new(
        claim.Id,
        claim.Reference,
        claim.PolicyNumber,
        claim.ClaimantName,
        claim.IncidentDate,
        claim.AmountClaimed,
        claim.Status,
        claim.AssignedToUserId,
        claim.AssignedToUser?.DisplayName,
        claim.IsPrivate,
        claim.CreatedAt);
}

public record ClaimDetailResponse(ClaimResponse Claim, IReadOnlyList<ClaimDocumentResponse> Documents)
{
    public static ClaimDetailResponse From(Claim claim) => new(
        ClaimResponse.From(claim),
        claim.Documents.Select(ClaimDocumentResponse.From).ToList());
}

// A document as seen from its claim: metadata and summary only, not the (potentially large) extracted text.
public record ClaimDocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    DocumentStatus Status,
    DateTime UploadedAt,
    DateTime? ProcessedAt,
    string? Summary)
{
    public static ClaimDocumentResponse From(Document document) => new(
        document.Id,
        document.FileName,
        document.ContentType,
        document.FileSize,
        document.Status,
        document.UploadedAt,
        document.ProcessedAt,
        document.Summary);
}
