using System.Text;
using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure;

public class PlainTextDocumentTextExtractor : IDocumentTextExtractor
{
    private const string SupportedContentType = "text/plain";

    public bool CanHandle(string contentType) =>
        string.Equals(contentType, SupportedContentType, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractTextAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content == null || content.Length == 0)
        {
            throw new ArgumentException("Document content cannot be null or empty.", nameof(content));
        }

        if (!CanHandle(contentType))
        {
            throw new NotSupportedException($"Content type '{contentType}' is not supported for text extraction.");
        }

        // Honours a byte-order mark, otherwise assumes UTF-8.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return (await reader.ReadToEndAsync(cancellationToken)).Trim();
    }
}
