using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;

public interface IDocumentRepository
{
    // Creates a new document in the database and return the created document's ID.
    Task<Guid> CreateDocumentAsync(Document document, CancellationToken cancellationToken = default);
}

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _context;

    public DocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> CreateDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document.Id;
    }
}
