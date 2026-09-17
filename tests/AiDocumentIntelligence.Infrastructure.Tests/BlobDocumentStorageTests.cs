using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class BlobDocumentStorageTests
{
    private readonly Mock<BlobContainerClient> _containerClientMock = new();
    private readonly Mock<BlobServiceClient> _serviceClientMock = new();

    public BlobDocumentStorageTests()
    {
        _serviceClientMock
            .Setup(s => s.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(_containerClientMock.Object);
    }

    private BlobDocumentStorage CreateSut()
    {
        var options = Options.Create(new AzureStorageOptions { ContainerName = "documents" });
        return new BlobDocumentStorage(_serviceClientMock.Object, options);
    }

    [Fact]
    public async Task UploadAsync_NullContent_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UploadAsync(null!, "file.txt", "text/plain"));

        Assert.Equal("content", ex.ParamName);
    }

    [Fact]
    public async Task UploadAsync_EmptyContent_ThrowsArgumentException()
    {
        var sut = CreateSut();
        using var content = new MemoryStream();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UploadAsync(content, "file.txt", "text/plain"));

        Assert.Equal("content", ex.ParamName);
    }

    [Fact]
    public async Task UploadAsync_ContentExceedsMaxSize_ThrowsArgumentException()
    {
        var sut = CreateSut();
        using var content = new OversizedStream((50 * 1024 * 1024) + 1);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UploadAsync(content, "file.txt", "text/plain"));

        Assert.Equal("content", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadAsync_InvalidContentType_ThrowsArgumentException(string? contentType)
    {
        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UploadAsync(content, "file.txt", contentType!));

        Assert.Equal("contentType", ex.ParamName);
    }

    [Fact]
    public async Task UploadAsync_ValidContent_UploadsToBlobAndReturnsBlobName()
    {
        var blobClientMock = new Mock<BlobClient>();
        blobClientMock
            .Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<AccessTier?>(),
                It.IsAny<StorageTransferOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        _containerClientMock
            .Setup(c => c.GetBlobClient("file.txt"))
            .Returns(blobClientMock.Object);

        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3]);

        var result = await sut.UploadAsync(content, "file.txt", "text/plain");

        Assert.Equal("file.txt", result);
        blobClientMock.Verify(b => b.UploadAsync(
            content,
            It.Is<BlobHttpHeaders>(h => h.ContentType == "text/plain"),
            null,
            null,
            null,
            null,
            default(StorageTransferOptions),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_BlobClientThrows_WrapsInInvalidOperationException()
    {
        var blobClientMock = new Mock<BlobClient>();
        blobClientMock
            .Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<AccessTier?>(),
                It.IsAny<StorageTransferOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("boom"));
        _containerClientMock
            .Setup(c => c.GetBlobClient("file.txt"))
            .Returns(blobClientMock.Object);

        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UploadAsync(content, "file.txt", "text/plain"));

        Assert.IsType<RequestFailedException>(ex.InnerException);
    }

    // Reports an oversized length without allocating the underlying bytes.
    private sealed class OversizedStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get; } = length;
        public override long Position { get; set; }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => Position;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
