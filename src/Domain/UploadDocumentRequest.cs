public record UploadDocumentRequest(
    Stream Content,
    string FileName,
    string ContentType,
    long FileSize);