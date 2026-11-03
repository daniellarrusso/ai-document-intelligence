using System.Text;
using AiDocumentIntelligence.Domain;
using UglyToad.PdfPig;

namespace AiDocumentIntelligence.Infrastructure;

public class PdfDocumentTextExtractor : IDocumentTextExtractor
{
    private const string SupportedContentType = "application/pdf";

    public bool CanHandle(string contentType) =>
        string.Equals(contentType, SupportedContentType, StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content == null || content.Length == 0)
        {
            throw new ArgumentException("Document content cannot be null or empty.", nameof(content));
        }

        if (!CanHandle(contentType))
        {
            throw new NotSupportedException($"Content type '{contentType}' is not supported for text extraction.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = PdfDocument.Open(content);
            var text = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(page.Text);
            }

            return Task.FromResult(text.ToString().Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Failed to extract text from the PDF document.", ex);
        }
    }
}
