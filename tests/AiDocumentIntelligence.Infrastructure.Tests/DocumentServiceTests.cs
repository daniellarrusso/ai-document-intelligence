using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IDocumentStorage> _storageMock = new();
    private readonly Mock<IDocumentProcessingQueue> _queueMock = new();

    private DocumentService CreateSut() =>
        new(_repositoryMock.Object, _storageMock.Object, _queueMock.Object);

    private static UploadDocumentRequest CreateRequest() =>
        new(new MemoryStream([1, 2, 3]), "file.txt", "text/plain", 3);

    [Fact]
    public async Task SaveDocumentAsync_ValidRequest_ReturnsDocumentWithProcessingStatusAndEnqueuesIt()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest());

        Assert.Equal(DocumentStatus.Processing, document.Status);
        _repositoryMock.Verify(r => r.UpdateStatusAsync(document.Id, DocumentStatus.Processing, null, It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(q => q.EnqueueAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDocumentAsync_UploadFails_DoesNotEnqueueDocument()
    {
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SaveDocumentAsync(CreateRequest()));

        _queueMock.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentAsync_DocumentExists_DeletesBlobThenRecordAndReturnsTrue()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "documents/x/original/file.txt" };
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(document.Id);

        Assert.True(result);
        _storageMock.Verify(s => s.DeleteAsync(document.BlobName, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentAsync_DocumentMissing_ReturnsFalseAndDeletesNothing()
    {
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(Guid.NewGuid());

        Assert.False(result);
        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentAsync_BlobDeleteFails_KeepsRecord()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b" };
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DeleteAsync("b", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteDocumentAsync(document.Id));

        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
