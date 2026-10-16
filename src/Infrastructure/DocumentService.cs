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

    public const int MaxSearchLength = 100;

    // Throws ArgumentException if the search text is too long. Blank search text means no text filter.
    public Task<List<Document>> GetDocumentsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, string? search = null, bool unmatchedOnly = false, CancellationToken cancellationToken = default)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (trimmedSearch?.Length > MaxSearchLength)
        {
            throw new ArgumentException($"Search text cannot exceed {MaxSearchLength} characters.");
        }

        return _documentRepository.GetDocumentsAsync(user, pageNumber, pageSize, trimmedSearch, unmatchedOnly, cancellationToken);
    }

    // The claims the document is linked to that the user may view. Null if the document doesn't exist or the
    // user may not view it.
    public async Task<List<Claim>?> GetLinkedClaimsAsync(CurrentUser user, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (await _documentRepository.GetDocumentForUserAsync(user, documentId, cancellationToken) == null)
        {
            return null;
        }

        return await _claimRepository.GetClaimsForDocumentAsync(user, documentId, cancellationToken);
    }

    // Links a document to a claim. NotFound if either is missing or not visible to the user (so existence isn't
    // revealed); Forbidden if both are visible but the user may not act on the claim. Linking twice is fine.
    public async Task<AccessOutcome> LinkToClaimAsync(CurrentUser user, Guid documentId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var outcome = await AuthorizeLinkChangeAsync(user, documentId, claimId, cancellationToken);
        if (outcome != AccessOutcome.Allowed)
        {
            return outcome;
        }

        // The foreign key needs the user row to exist, even if they never called /api/me.
        await _userService.RecordSignInAsync(user, cancellationToken);
        await _documentRepository.LinkToClaimAsync(documentId, claimId, user.Id, cancellationToken);

        return AccessOutcome.Allowed;
    }

    // Same outcomes as LinkToClaimAsync. Unlinking a document that isn't linked is fine.
    public async Task<AccessOutcome> UnlinkFromClaimAsync(CurrentUser user, Guid documentId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var outcome = await AuthorizeLinkChangeAsync(user, documentId, claimId, cancellationToken);
        if (outcome != AccessOutcome.Allowed)
        {
            return outcome;
        }

        await _documentRepository.UnlinkFromClaimAsync(documentId, claimId, cancellationToken);

        return AccessOutcome.Allowed;
    }

    // Changing a link needs two things: sight of the document, and the right to act on the claim.
    private async Task<AccessOutcome> AuthorizeLinkChangeAsync(CurrentUser user, Guid documentId, Guid claimId, CancellationToken cancellationToken)
    {
        if (await _documentRepository.GetDocumentForUserAsync(user, documentId, cancellationToken) == null)
        {
            return AccessOutcome.NotFound;
        }

        var claim = await _claimRepository.GetClaimAsync(user, claimId, cancellationToken);
        if (claim == null)
        {
            return AccessOutcome.NotFound;
        }

        return ClaimAccess.CanAct(user, claim) ? AccessOutcome.Allowed : AccessOutcome.Forbidden;
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
            UploadedByUserId = uploader.Id
        };

        // The foreign key needs the user row to exist, even if they never called /api/me.
        await _userService.RecordSignInAsync(uploader, cancellationToken);
        await _documentRepository.CreateDocumentAsync(document, claimId, uploader.Id, cancellationToken);

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

        var linkedClaims = await _claimRepository.GetAllClaimsForDocumentAsync(id, cancellationToken);
        if (!ClaimAccess.CanActOnDocument(user, document, linkedClaims))
        {
            return AccessOutcome.Forbidden;
        }

        await _documentStorage.DeleteAsync(document.BlobName, cancellationToken);
        await _documentRepository.DeleteDocumentAsync(id, cancellationToken);

        return AccessOutcome.Allowed;
    }
}
