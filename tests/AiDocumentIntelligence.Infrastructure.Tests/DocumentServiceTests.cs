using AiDocumentIntelligence.Domain;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IDocumentStorage> _storageMock = new();
    private readonly Mock<IClaimRepository> _claimRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IDocumentProcessingQueue> _queueMock = new();

    private DocumentService CreateSut() =>
        new(_repositoryMock.Object, _claimRepositoryMock.Object, _storageMock.Object, _queueMock.Object, new UserService(_userRepositoryMock.Object));

    private static UploadDocumentRequest CreateRequest() =>
        new(new MemoryStream([1, 2, 3]), "file.txt", "text/plain", 3);

    [Fact]
    public async Task SaveDocumentAsync_ValidRequest_ReturnsDocumentWithProcessingStatusAndEnqueuesIt()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(DocumentStatus.Processing, document.Status);
        _repositoryMock.Verify(r => r.UpdateStatusAsync(document.Id, DocumentStatus.Processing, null, It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(q => q.EnqueueAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDocumentAsync_WithClaimId_AttachesDocumentToClaim()
    {
        var claimId = Guid.NewGuid();
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler, claimId);

        Assert.Equal(claimId, document.ClaimId);
        _repositoryMock.Verify(r => r.CreateDocumentAsync(It.Is<Document>(d => d.ClaimId == claimId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDocumentAsync_WithoutClaimId_LeavesDocumentUnattached()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Null(document.ClaimId);
    }

    [Fact]
    public async Task SaveDocumentAsync_UploadFails_DoesNotEnqueueDocument()
    {
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler));

        _queueMock.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentAsync_StandaloneOwnDocument_DeletesBlobThenRecordAndReturnsAllowed()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "documents/x/original/file.txt", UploadedByUserId = TestUsers.Handler.Id };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Handler, document.Id);

        Assert.Equal(AccessOutcome.Allowed, result);
        _storageMock.Verify(s => s.DeleteAsync(document.BlobName, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentAsync_DocumentMissingOrNotVisible_ReturnsNotFoundAndDeletesNothing()
    {
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Handler, Guid.NewGuid());

        Assert.Equal(AccessOutcome.NotFound, result);
        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentAsync_BlobDeleteFails_KeepsRecord()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b", UploadedByUserId = TestUsers.Handler.Id };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DeleteAsync("b", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteDocumentAsync(TestUsers.Handler, document.Id));

        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDocumentAsync_RecordsUploaderAndEnsuresTheyExist()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(TestUsers.Handler.Id, document.UploadedByUserId);
        _userRepositoryMock.Verify(r => r.UpsertAsync(It.Is<AppUser>(u => u.Id == TestUsers.Handler.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ClaimDocumentOnOwnClaim_IsAllowed()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b", ClaimId = claim.Id };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _claimRepositoryMock.Setup(r => r.GetClaimAsync(TestUsers.Handler, claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Handler, document.Id);

        Assert.Equal(AccessOutcome.Allowed, result);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ReaderCanSeeButNotDelete_ReturnsForbiddenAndDeletesNothing()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b" };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Reader, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Reader, document.Id);

        Assert.Equal(AccessOutcome.Forbidden, result);
        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ClaimNoLongerActable_ReturnsForbidden()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b", ClaimId = Guid.NewGuid() };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Handler, document.Id);

        Assert.Equal(AccessOutcome.Forbidden, result);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
