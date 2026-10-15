namespace AiDocumentIntelligence.Domain;

public class Document
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSize { get; set; }

    public string BlobName { get; set; } = null!;

    public DocumentStatus Status { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? ExtractedText { get; set; }

    public string? Summary { get; set; }

    // Null for documents that aren't attached to a claim (including all documents uploaded before claims existed).
    public Guid? ClaimId { get; set; }
}
