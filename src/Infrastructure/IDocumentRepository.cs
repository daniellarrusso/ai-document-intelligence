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
}
