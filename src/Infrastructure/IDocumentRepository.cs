using AiDocumentIntelligence.Domain;

public interface IDocumentRepository
{
    // Unfiltered lookup for trusted background work (the processing worker). Request handling must use
    // GetDocumentForUserAsync so visibility rules apply.
    Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // The document if the user may view it (see ClaimAccess), otherwise null.
    Task<Document?> GetDocumentForUserAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default);

    // Newest first, limited to the documents the user may view, with pagination. A non-null search is matched
    // case-insensitively as a substring of the file name; unmatchedOnly restricts the result to documents that
    // are linked to no claim.
    Task<List<Document>> GetDocumentsAsync(CurrentUser user, int pageNumber = 1, int pageSize = 10, string? search = null, bool unmatchedOnly = false, CancellationToken cancellationToken = default);

    // Creates a new document in the database and returns its ID. With a claimId the document and its link to
    // that claim are saved together, so a failure can't leave an unmatched orphan.
    Task<Guid> CreateDocumentAsync(Document document, Guid? claimId = null, Guid? linkedByUserId = null, CancellationToken cancellationToken = default);

    // Links the document to the claim. Linking twice is a no-op. Callers must have authorised the change.
    Task LinkToClaimAsync(Guid documentId, Guid claimId, Guid linkedByUserId, CancellationToken cancellationToken = default);

    // Removes the link. A no-op if there is none. Callers must have authorised the change.
    Task UnlinkFromClaimAsync(Guid documentId, Guid claimId, CancellationToken cancellationToken = default);

    // Delete a document from the database by its ID.
    Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);

    // Sets a document's status (and processed timestamp). No-op if the document does not exist.
    Task UpdateStatusAsync(Guid id, DocumentStatus status, DateTime? processedAt = null, CancellationToken cancellationToken = default);

    // Stores the extracted text and advances the document to GeneratingSummary. No-op if the document does not exist.
    Task SaveExtractedTextAsync(Guid id, string extractedText, CancellationToken cancellationToken = default);

    // Marks the document Completed. Called once summarization has been attempted, whether or not it succeeded.
    Task MarkCompletedAsync(Guid id, DateTime processedAt, CancellationToken cancellationToken = default);

    // Stores a generated summary. Best-effort enrichment: does not affect document status. No-op if the document does not exist.
    Task UpdateSummaryAsync(Guid id, string summary, CancellationToken cancellationToken = default);
}
