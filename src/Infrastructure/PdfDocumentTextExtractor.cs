using System.Text;
using AiDocumentIntelligence.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AiDocumentIntelligence.Infrastructure;

public class PdfDocumentTextExtractor : IDocumentTextExtractor
{
    private const string SupportedContentType = "application/pdf";

    // page.Text is just the page's letters in the order they were drawn: no line breaks, and words that were
    // drawn separately run together ("LimitedAspect House", "ADAMS50 REDVERS ROAD"). Content-order extraction
    // rebuilds words and lines from the letters' positions, which is what matching and search need.
    private static string ExtractPageText(Page page)
    {
        try
        {
            return ContentOrderTextExtractor.GetText(page);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The layout analysis can fail on unusual pages; the raw text is still better than none.
            return page.Text;
        }
    }

    public Task<string> ExtractTextAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content == null || content.Length == 0)
        {
            throw new ArgumentException("Document content cannot be null or empty.", nameof(content));
        }

        if (!string.Equals(contentType, SupportedContentType, StringComparison.OrdinalIgnoreCase))
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
                text.AppendLine(ExtractPageText(page));
            }

            return Task.FromResult(text.ToString().Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Failed to extract text from the PDF document.", ex);
        }
    }
}
