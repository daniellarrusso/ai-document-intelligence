using AiDocumentIntelligence.Domain;
// Now create a service class to save Document in DocumentRepository and then call BlobDocumentStorage to upload to blob storage

public class DocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorage _documentStorage;

    public DocumentService(IDocumentRepository documentRepository, IDocumentStorage documentStorage)
    {
        _documentRepository = documentRepository;
        _documentStorage = documentStorage;
    }

    public async Task<string> SaveDocumentAsync(UploadDocumentRequest file, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.NewGuid();

        var blobName = $"documents/{documentId}/original/{file.FileName}";

        await _documentStorage.UploadAsync(
            file.Content,
            blobName,
            file.ContentType,
            cancellationToken);

        var document = new Document
        {
            Id = documentId,
            FileName = file.FileName,
            BlobName = blobName,
            ContentType = file.ContentType,
            FileSize = file.FileSize,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded
        };

        await _documentRepository.CreateDocumentAsync(document, cancellationToken);

        return blobName;
    }
}