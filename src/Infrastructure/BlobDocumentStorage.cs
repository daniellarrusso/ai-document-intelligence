using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

public class BlobDocumentStorage : IDocumentStorage
{
    private readonly BlobContainerClient _container;

    public BlobDocumentStorage(BlobServiceClient blobServiceClient, IOptions<AzureStorageOptions> options)
    {
        _container = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        _container.CreateIfNotExists();
    }


    public async Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken cancellationToken = default)
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

        var blob = _container.GetBlobClient(blobName);

        try
        {
            await blob.UploadAsync(
                content,
                new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to upload the document to blob storage.", ex);
        }

        return blobName;
    }

    public async Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name cannot be null or empty.", nameof(blobName));
        }

        var blob = _container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException($"The document '{blobName}' was not found in blob storage.");
        }

        try
        {
            return await blob.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to download the document from blob storage.", ex);
        }
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        // Implement delete logic here
        throw new NotImplementedException();
    }
}