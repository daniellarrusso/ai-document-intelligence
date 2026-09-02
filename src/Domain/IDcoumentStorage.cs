public interface IDocumentStorage
{
    Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
