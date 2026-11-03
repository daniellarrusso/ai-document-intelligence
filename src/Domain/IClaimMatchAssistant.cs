namespace AiDocumentIntelligence.Domain;

// An AI judgement of which of the candidate claims a document belongs to. The result can only ever name
// candidates that were passed in, so a confused or manipulated model cannot invent a claim.
public interface IClaimMatchAssistant
{
    // Throws InvalidOperationException if the AI service fails.
    Task<IReadOnlyList<ClaimMatch>> FindMatchesAsync(string documentText, IReadOnlyList<Claim> candidates, CancellationToken cancellationToken = default);
}
