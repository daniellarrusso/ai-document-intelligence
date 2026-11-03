namespace AiDocumentIntelligence.Domain;

// An upload the file type policy refused; the message is safe to show to the caller.
public class InvalidFileException(string message) : ArgumentException(message);
