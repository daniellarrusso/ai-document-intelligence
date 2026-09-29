public interface IDocumentTextExtractor
{
    // Extracts plain text from a document's content. Throws NotSupportedException for content types
    // this extractor cannot handle.
    Task<string> ExtractTextAsync(Stream content, string contentType, CancellationToken cancellationToken = default);
}
