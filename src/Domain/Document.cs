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
}
