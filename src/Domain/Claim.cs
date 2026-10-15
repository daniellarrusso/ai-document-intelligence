namespace AiDocumentIntelligence.Domain;

public class Claim
{
    public const int MaxReferenceLength = 50;
    public const int MaxPolicyNumberLength = 50;
    public const int MaxNameLength = 200;
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;

    public Guid Id { get; set; }

    public string Reference { get; set; } = null!;

    public string PolicyNumber { get; set; } = null!;

    public string ClaimantName { get; set; } = null!;

    public DateOnly IncidentDate { get; set; }

    public decimal AmountClaimed { get; set; }

    public ClaimStatus Status { get; set; }

    // Legacy free-text assignee, superseded by AssignedToUserId; removed once the assignment endpoints use users.
    public string? AssignedTo { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    // Private claims are hidden from Readers; only the assigned Handler and Admins can see them.
    public bool IsPrivate { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Document> Documents { get; set; } = [];
}
