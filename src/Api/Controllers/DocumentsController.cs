using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly ILogger<DocumentsController> _logger;
    private readonly DocumentService _documentService;

    public DocumentsController(ILogger<DocumentsController> logger, DocumentService documentService)
    {
        _logger = logger;
        _documentService = documentService;
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
            file.Length), cancellationToken);

        return Ok(document);
    }
}
