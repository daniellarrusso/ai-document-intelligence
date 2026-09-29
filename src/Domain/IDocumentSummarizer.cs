public interface IDocumentSummarizer
{
    // Produces a short summary of the supplied document text.
    Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default);
}
