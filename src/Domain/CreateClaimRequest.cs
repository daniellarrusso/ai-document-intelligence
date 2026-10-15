namespace AiDocumentIntelligence.Domain;

public record CreateClaimRequest(
    string? PolicyNumber,
    string? ClaimantName,
    DateOnly IncidentDate,
    decimal AmountClaimed,
    string? AssignedTo);
