public class DocumentStorage : IDocumentStorage
{
    public Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken cancellationToken = default)
    {
        // validate file supplied
        if (content == null || content.Length == 0)
        {
            throw new ArgumentException("File content cannot be null or empty.", nameof(content));
        }
        // max size less than 50 MB
        if (content.Length > 50 * 1024 * 1024)
        {
            throw new ArgumentException("File size cannot exceed 50 MB.", nameof(content));
        }

        // validate content type
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type cannot be null or empty.", nameof(contentType));
        }
        // implement upload to Azurite Blob Storage logic here
        // For demonstration purposes, we'll just return the blob name as the URL
        return Task.FromResult(blobName);
    }

    public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        // Implement download logic here
        throw new NotImplementedException();
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        // Implement delete logic here
        throw new NotImplementedException();
    }
}