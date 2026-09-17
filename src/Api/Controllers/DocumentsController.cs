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
    public IActionResult Upload([FromForm] IFormFile file)
    {
        // Implement logic to upload a document to the database
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be null or empty.");
        }

        var uploadResult = _documentService.SaveDocumentAsync(new UploadDocumentRequest(
            file.OpenReadStream(),
            file.FileName,
            file.ContentType,
            file.Length)).Result;

        if (uploadResult == null)
        {
            return BadRequest("File upload failed.");
        }

        return new JsonResult(uploadResult);
    }
}