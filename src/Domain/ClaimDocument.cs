namespace AiDocumentIntelligence.Domain;

// The link between a claim and a document. A document can sit on many claims and a claim has many documents,
// so the relationship lives in this join row rather than as a foreign key on either side. A document with no
// rows here is "unmatched".
public class ClaimDocument
{
    public Guid ClaimId { get; set; }

    public Guid DocumentId { get; set; }

    public DateTime LinkedAt { get; set; }

    // Who made the link; null if that user has since been removed.
    public Guid? LinkedByUserId { get; set; }
}
