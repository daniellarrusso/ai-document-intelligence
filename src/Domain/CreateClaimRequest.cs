namespace AiDocumentIntelligence.Domain;

// AssignedToUserId is optional: when omitted the creator is the assignee. IsPrivate hides the claim from Readers.
public record CreateClaimRequest(
    string? PolicyNumber,
    string? ClaimantName,
    DateOnly IncidentDate,
    decimal AmountClaimed,
    Guid? AssignedToUserId = null,
    bool IsPrivate = false);
