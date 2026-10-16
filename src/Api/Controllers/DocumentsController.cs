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
    private readonly IDocumentQuestionAnswerer _questionAnswerer;

    public DocumentsController(ILogger<DocumentsController> logger, DocumentService documentService, IDocumentQuestionAnswerer questionAnswerer)
    {
        _logger = logger;
        _documentService = documentService;
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
            return Ok(documents);
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
