using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.Options;

namespace AiDocumentIntelligence.Infrastructure;

public class OllamaDocumentSummarizer : IDocumentSummarizer
{
    // Small local models lose coherence and get slow well before their nominal context window.
    // Capping the input keeps latency predictable and the summary focused on the start of the document.
    private const int MaxInputCharacters = 8000;

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaDocumentSummarizer(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _model = options.Value.Model;
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
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                new OllamaGenerateRequest(_model, prompt, false),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
            if (result?.Response == null)
            {
                throw new InvalidOperationException("Ollama returned an empty response.");
            }

            return result.Response.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to summarize the document using Ollama.", ex);
        }
    }

    private record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response);
}
