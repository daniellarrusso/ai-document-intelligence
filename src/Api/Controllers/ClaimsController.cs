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
        try
        {
            var claim = await _claimService.CreateClaimAsync(request, cancellationToken);

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
            var claims = await _claimService.GetClaimsAsync(pageNumber, pageSize, q, status, cancellationToken);

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
        var claim = await _claimService.GetClaimWithDocumentsAsync(id, cancellationToken);

        return claim == null ? NotFound() : Ok(ClaimDetailResponse.From(claim));
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPost("{id}/documents", Name = "UploadClaimDocument")]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be null or empty.");
        }

        if (!await _claimService.ClaimExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.SaveDocumentAsync(new UploadDocumentRequest(
            stream,
            file.FileName,
            file.ContentType,
            file.Length), id, cancellationToken);

        return Ok(ClaimDocumentResponse.From(document));
    }
}

public record ClaimResponse(
    Guid Id,
    string Reference,
    string PolicyNumber,
    string ClaimantName,
    DateOnly IncidentDate,
    decimal AmountClaimed,
    ClaimStatus Status,
    string? AssignedTo,
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
        claim.AssignedTo,
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
