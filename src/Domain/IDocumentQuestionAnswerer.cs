namespace AiDocumentIntelligence.Domain;

public interface IDocumentQuestionAnswerer
{
    // Answers a question using only the most relevant indexed chunks of the document.
    // Returns null if the document has no indexed content to answer from.
    Task<DocumentAnswer?> AskAsync(Guid documentId, string question, CancellationToken cancellationToken = default);
}

public record DocumentAnswer(string Answer, IReadOnlyList<DocumentChunkMatch> Sources);

// Score is cosine similarity (higher is more relevant).
public record DocumentChunkMatch(int ChunkIndex, string Text, double Score);
