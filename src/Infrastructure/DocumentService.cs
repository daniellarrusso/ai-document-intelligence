using AiDocumentIntelligence.Domain;
// Now create a service class to save Document in DocumentRepository and then call BlobDocumentStorage to upload to blob storage

public class DocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorage _documentStorage;
    private readonly IDocumentProcessingQueue _processingQueue;

    public DocumentService(IDocumentRepository documentRepository, IDocumentStorage documentStorage, IDocumentProcessingQueue processingQueue)
    {
        _documentRepository = documentRepository;
        _documentStorage = documentStorage;
        _processingQueue = processingQueue;
    }

    public async Task<List<Document>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        return await _documentRepository.GetDocumentsAsync(pageNumber, pageSize, cancellationToken);
    }

    public async Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _documentRepository.GetDocumentByIdAsync(id, cancellationToken);
    }

    public async Task<Document> SaveDocumentAsync(UploadDocumentRequest file, CancellationToken cancellationToken = default)
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

        // Queued for processing: mark Processing before enqueueing so the worker's result can't be overwritten.
        await _documentRepository.UpdateStatusAsync(documentId, DocumentStatus.Processing, null, cancellationToken);
        document.Status = DocumentStatus.Processing;
        await _processingQueue.EnqueueAsync(documentId, cancellationToken);

        return document;
    }

    // Returns false if the document does not exist. The blob is removed first so a storage failure
    // leaves the record in place and the delete can be retried.
    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetDocumentByIdAsync(id, cancellationToken);
        if (document == null)
        {
            return false;
        }

        await _documentStorage.DeleteAsync(document.BlobName, cancellationToken);
        await _documentRepository.DeleteDocumentAsync(id, cancellationToken);

        return true;
    }
}
