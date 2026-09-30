using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.AI;

namespace AiDocumentIntelligence.Infrastructure;

public class AIChatDocumentSummarizer : IDocumentSummarizer
{
    // Small local models lose coherence and get slow well before their nominal context window.
    // Capping the input keeps latency predictable and the summary focused on the start of the document.
    private const int MaxInputCharacters = 8000;

    private readonly IChatClient _chatClient;

    public AIChatDocumentSummarizer(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Document text cannot be null or empty.", nameof(text));
        }

        var truncated = text.Length > MaxInputCharacters ? text[..MaxInputCharacters] : text;
        var prompt = $"Summarize the following document in 2-3 sentences.\n\nDocument:\n{truncated}";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                throw new InvalidOperationException("Ollama returned an empty response.");
            }

            return response.Text.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to summarize the document using Ollama.", ex);
        }
    }
}
