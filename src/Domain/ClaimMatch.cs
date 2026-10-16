namespace AiDocumentIntelligence.Domain;

// What kind of evidence tied a document to a claim. Serialised as an integer.
public enum MatchMethod
{
    Reference = 0,
    PolicyNumber = 1,
    ClaimantName = 2,
    Ai = 3
}

// A claim a document appears to belong to, with a human-readable reason shown to the user who confirms it.
public record ClaimMatch(Claim Claim, MatchMethod Method, string Detail);
