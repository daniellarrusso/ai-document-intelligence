using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
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
    public IActionResult Get()
    {
        // Implement logic to retrieve a document from database
        var documents = _documentService.GetDocumentsAsync().Result;
        return Ok(documents);
    }

    [HttpGet("{id}", Name = "GetDocumentById")]
    public IActionResult GetDocumentById(Guid id)
    {
        // Implement logic to retrieve a document from database
        var document = _documentService.GetDocumentByIdAsync(id).Result;
        if (document == null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    [HttpPost(Name = "UploadDocument")]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be null or empty.");
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.SaveDocumentAsync(new UploadDocumentRequest(
            stream,
            file.FileName,
            file.ContentType,
            file.Length), cancellationToken: cancellationToken);

        return Ok(document);
    }

    [HttpDelete("{id}", Name = "DeleteDocument")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _documentService.DeleteDocumentAsync(id, cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id}/ask", Name = "AskDocument")]
    public async Task<IActionResult> Ask(Guid id, [FromBody] AskDocumentRequest request, CancellationToken cancellationToken)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question))
        {
            return BadRequest("Question cannot be empty.");
        }

        if (question.Length > DocumentQuestionAnswerer.MaxQuestionLength)
        {
            return BadRequest($"Question cannot exceed {DocumentQuestionAnswerer.MaxQuestionLength} characters.");
        }

        var document = await _documentService.GetDocumentByIdAsync(id, cancellationToken);
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
