using AiDocumentIntelligence.Domain;

public interface IDocumentRepository
{
    Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    // Retrieve a list of documents from the database with optional pagination.
    Task<List<Document>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    // Creates a new document in the database and return the created document's ID.
    Task<Guid> CreateDocumentAsync(Document document, CancellationToken cancellationToken = default);

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
