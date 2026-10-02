namespace AiDocumentIntelligence.Domain;

public class Claim
{
    public Guid Id { get; set; }

    public string Reference { get; set; } = null!;

    public string PolicyNumber { get; set; } = null!;

    public string ClaimantName { get; set; } = null!;

    public DateOnly IncidentDate { get; set; }

    public decimal AmountClaimed { get; set; }

    public ClaimStatus Status { get; set; }

    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Document> Documents { get; set; } = [];
}
