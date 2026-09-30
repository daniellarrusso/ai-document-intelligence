namespace AiDocumentIntelligence.Domain;

public enum DocumentStatus
{
    Uploaded = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,

    // Appended rather than inserted: Status is persisted as a plain integer, so existing rows'
    // values for Completed/Failed must not shift.
    ExtractingText = 4,
    GeneratingSummary = 5
}