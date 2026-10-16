using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using AiDocumentIntelligence.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.CanRead)]
public class DocumentsController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly ILogger<DocumentsController> _logger;
    private readonly DocumentService _documentService;
    private readonly DocumentMatchService _matchService;
    private readonly IDocumentQuestionAnswerer _questionAnswerer;

    public DocumentsController(ILogger<DocumentsController> logger, DocumentService documentService, DocumentMatchService matchService, IDocumentQuestionAnswerer questionAnswerer)
    {
        _logger = logger;
        _documentService = documentService;
        _matchService = matchService;
        _questionAnswerer = questionAnswerer;
    }

    [HttpGet(Name = "GetDocuments")]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? q = null,
        [FromQuery] bool unmatched = false)
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

        try
        {
            var documents = await _documentService.GetDocumentsAsync(user, pageNumber, pageSize, q, unmatched, cancellationToken);
            var counts = await _documentService.GetLinkCountsAsync(documents.Select(d => d.Id).ToList(), cancellationToken);

            return Ok(documents.Select(d => DocumentListItem.From(d, counts.GetValueOrDefault(d.Id))));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // The claims this document is linked to that the caller may see (an empty list means unmatched).
    [HttpGet("{id}/claims", Name = "GetDocumentClaims")]
    public async Task<IActionResult> GetClaims(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        var claims = await _documentService.GetLinkedClaimsAsync(user, id, cancellationToken);

        return claims == null ? NotFound() : Ok(claims.Select(ClaimResponse.From));
    }

    // Which claims the document looks like it belongs to, and whether its current links look right. Only
    // suggests; linking is a separate, confirmed call. ai=true lets the AI try when the text checks find nothing.
    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpGet("{id}/match-suggestions", Name = "GetDocumentMatchSuggestions")]
    public async Task<IActionResult> GetMatchSuggestions(Guid id, CancellationToken cancellationToken, [FromQuery] bool ai = false)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        var scan = await _matchService.ScanAsync(user, id, ai, cancellationToken);

        return scan == null ? NotFound() : Ok(MatchScanResponse.From(scan));
    }

    [HttpGet("{id}", Name = "GetDocumentById")]
    public async Task<IActionResult> GetDocumentById(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        var document = await _documentService.GetDocumentByIdAsync(user, id, cancellationToken);
        if (document == null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpPost(Name = "UploadDocument")]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be null or empty.");
        }

        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.SaveDocumentAsync(new UploadDocumentRequest(
            stream,
            file.FileName,
            file.ContentType,
            file.Length), user, cancellationToken: cancellationToken);

        return Ok(document);
    }

    [Authorize(Policy = AuthorizationPolicies.CanWrite)]
    [HttpDelete("{id}", Name = "DeleteDocument")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        return await _documentService.DeleteDocumentAsync(user, id, cancellationToken) switch
        {
            AccessOutcome.Allowed => NoContent(),
            AccessOutcome.Forbidden => Forbid(),
            _ => NotFound()
        };
    }

    [HttpPost("{id}/ask", Name = "AskDocument")]
    public async Task<IActionResult> Ask(Guid id, [FromBody] AskDocumentRequest request, CancellationToken cancellationToken)
    {
        if (User.GetCurrentUser() is not { } user)
        {
            return Forbid();
        }

        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question))
        {
            return BadRequest("Question cannot be empty.");
        }

        if (question.Length > DocumentQuestionAnswerer.MaxQuestionLength)
        {
            return BadRequest($"Question cannot exceed {DocumentQuestionAnswerer.MaxQuestionLength} characters.");
        }

        var document = await _documentService.GetDocumentByIdAsync(user, id, cancellationToken);
        if (document == null)
        {
            return NotFound();
        }

        try
        {
            var answer = await _questionAnswerer.AskAsync(id, question, cancellationToken);

            return answer == null
                ? Conflict("This document has no searchable content. It may still be processing or indexing may have failed.")
                : Ok(answer);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to answer a question about document {DocumentId}", id);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The AI service is currently unavailable. Please try again.");
        }
    }
}

public record AskDocumentRequest(string? Question);

// A document in the list: everything the document endpoint returns, plus how many claims it is linked to
// (0 = unmatched).
public record DocumentListItem(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    string BlobName,
    DocumentStatus Status,
    DateTime UploadedAt,
    DateTime? ProcessedAt,
    string? ExtractedText,
    string? Summary,
    Guid? UploadedByUserId,
    int ClaimCount)
{
    public static DocumentListItem From(Document d, int claimCount) => new(
        d.Id, d.FileName, d.ContentType, d.FileSize, d.BlobName, d.Status, d.UploadedAt, d.ProcessedAt,
        d.ExtractedText, d.Summary, d.UploadedByUserId, claimCount);
}

public record ClaimMatchResponse(ClaimResponse Claim, MatchMethod Method, string Detail)
{
    public static ClaimMatchResponse From(ClaimMatch match) => new(ClaimResponse.From(match.Claim), match.Method, match.Detail);
}

// A current link and, if the text supports it, why. Supported = false means the text never mentions the claim.
public record LinkedClaimCheckResponse(ClaimResponse Claim, bool Supported, MatchMethod? Method, string? Detail)
{
    public static LinkedClaimCheckResponse From(LinkedClaimCheck check) =>
        new(ClaimResponse.From(check.Claim), check.Evidence != null, check.Evidence?.Method, check.Evidence?.Detail);
}

public record MatchScanResponse(
    bool TextAvailable,
    IReadOnlyList<ClaimMatchResponse> Suggestions,
    IReadOnlyList<LinkedClaimCheckResponse> LinkedClaims,
    bool AiAttempted,
    bool AiFailed)
{
    public static MatchScanResponse From(MatchScan scan) => new(
        scan.TextAvailable,
        scan.Suggestions.Select(ClaimMatchResponse.From).ToList(),
        scan.LinkedClaims.Select(LinkedClaimCheckResponse.From).ToList(),
        scan.AiAttempted,
        scan.AiFailed);
}
