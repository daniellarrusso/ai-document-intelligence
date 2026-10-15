using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
// Now create a service class to save Document in DocumentRepository and then call BlobDocumentStorage to upload to blob storage

public class DocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IClaimRepository _claimRepository;
    private readonly IDocumentStorage _documentStorage;
    private readonly IDocumentProcessingQueue _processingQueue;
    private readonly UserService _userService;

    public DocumentService(
        IDocumentRepository documentRepository,
        IClaimRepository claimRepository,
        IDocumentStorage documentStorage,
        IDocumentProcessingQueue processingQueue,
        UserService userService)
    {
        _documentRepository = documentRepository;
        _claimRepository = claimRepository;
        _documentStorage = documentStorage;
        _processingQueue = processingQueue;
        _userService = userService;
    }

    public async Task<List<Document>> GetDocumentsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        return await _documentRepository.GetDocumentsAsync(user, pageNumber, pageSize, cancellationToken);
    }

    // Null if the document doesn't exist or the user may not view it.
    public async Task<Document?> GetDocumentByIdAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        return await _documentRepository.GetDocumentForUserAsync(user, id, cancellationToken);
    }

    // The caller is recorded as the uploader. The controller is responsible for checking the user may act
    // on claimId (ClaimService.AuthorizeActionAsync) before attaching a document to it.
    public async Task<Document> SaveDocumentAsync(UploadDocumentRequest file, CurrentUser uploader, Guid? claimId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uploader);

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
            Status = DocumentStatus.Uploaded,
            ClaimId = claimId,
            UploadedByUserId = uploader.Id
        };

        // The foreign key needs the user row to exist, even if they never called /api/me.
        await _userService.RecordSignInAsync(uploader, cancellationToken);
        await _documentRepository.CreateDocumentAsync(document, cancellationToken);

        // Queued for processing: mark Processing before enqueueing so the worker's result can't be overwritten.
        await _documentRepository.UpdateStatusAsync(documentId, DocumentStatus.Processing, null, cancellationToken);
        document.Status = DocumentStatus.Processing;
        await _processingQueue.EnqueueAsync(documentId, cancellationToken);

        return document;
    }

    // NotFound if the document doesn't exist or the user may not view it. The blob is removed first so a
    // storage failure leaves the record in place and the delete can be retried.
    public async Task<AccessOutcome> DeleteDocumentAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetDocumentForUserAsync(user, id, cancellationToken);
        if (document == null)
        {
            return AccessOutcome.NotFound;
        }

        var canAct = document.ClaimId is { } claimId
            ? await CanActOnClaimAsync(user, claimId, cancellationToken)
            : ClaimAccess.CanActOnStandaloneDocument(user, document);
        if (!canAct)
        {
            return AccessOutcome.Forbidden;
        }

        await _documentStorage.DeleteAsync(document.BlobName, cancellationToken);
        await _documentRepository.DeleteDocumentAsync(id, cancellationToken);

        return AccessOutcome.Allowed;
    }

    private async Task<bool> CanActOnClaimAsync(CurrentUser user, Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await _claimRepository.GetClaimAsync(user, claimId, cancellationToken);

        return claim != null && ClaimAccess.CanAct(user, claim);
    }
}
