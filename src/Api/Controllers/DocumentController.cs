using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DocumentController : ControllerBase
{
    private readonly ILogger<DocumentController> _logger;
    private readonly DocumentService _documentService;
    private readonly AppDbContext _context;

    public DocumentController(ILogger<DocumentController> logger, AppDbContext context, DocumentService documentService)
    {
        _logger = logger;
        _context = context;
        _documentService = documentService;
    }

    [HttpGet(Name = "GetDocument")]
    public IActionResult Get()
    {
        // Implement logic to retrieve a document from database
        var documents = _context.Documents.ToList();
        return Ok(documents);
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