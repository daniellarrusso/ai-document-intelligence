namespace AiDocumentIntelligence.Domain;

public interface IDocumentIndexer
{
    // Splits the document text into chunks, embeds them and stores them for retrieval, replacing any
    // chunks previously indexed for the document.
    Task IndexAsync(Guid documentId, string text, CancellationToken cancellationToken = default);
}
