using System.Net;
using System.Net.Http.Headers;
using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Api.Tests;

public class DocumentContentApiTests : IDisposable
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient Client(string roles = AppRoles.Reader)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "reader");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    private void StoreDocument(string fileName, string contentType, byte[] bytes)
    {
        var document = new Document { Id = DocumentId, FileName = fileName, ContentType = contentType, BlobName = "documents/x/original/f" };
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _factory.Storage
            .Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes));
    }

    [Fact]
    public async Task GetContent_PreviewableType_ServesInlineWithStoredTypeAndNosniff()
    {
        StoreDocument("damage photo.png", "image/png", [1, 2, 3, 4]);
        using var client = Client();

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("private", response.Headers.CacheControl!.ToString());
        Assert.Equal([1, 2, 3, 4], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetContent_UnknownStoredType_IsOfferedAsDownloadNotInline()
    {
        StoreDocument("legacy.bin", "application/octet-stream", [1]);
        using var client = Client();

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/content");

        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task Download_PreviewableType_IsAttachment()
    {
        StoreDocument("a.pdf", "application/pdf", [1, 2]);
        using var client = Client();

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/download");

        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("a.pdf", response.Content.Headers.ContentDisposition.FileName);
    }

    [Fact]
    public async Task GetContent_RangeRequest_Returns206WithRequestedBytes()
    {
        StoreDocument("clip.mp4", "video/mp4", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{DocumentId}/content");
        request.Headers.Range = new RangeHeaderValue(2, 4);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([2, 3, 4], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetContent_DocumentNotVisibleToCaller_Returns404WithoutTouchingStorage()
    {
        _factory.Documents
            .Setup(r => r.GetDocumentForUserAsync(It.IsAny<CurrentUser>(), DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var client = Client();

        using var response = await client.GetAsync($"/api/documents/{DocumentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _factory.Storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_DisallowedFileType_Returns400WithReason()
    {
        using var client = Client(AppRoles.Handler);
        using var form = new MultipartFormDataContent { { new ByteArrayContent("MZ\x90\0"u8.ToArray()), "file", "setup.exe" } };

        using var response = await client.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_ExecutableRenamedToPdf_Returns400()
    {
        using var client = Client(AppRoles.Handler);
        using var form = new MultipartFormDataContent { { new ByteArrayContent("MZ\x90\0"u8.ToArray()), "file", "invoice.pdf" } };

        using var response = await client.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
