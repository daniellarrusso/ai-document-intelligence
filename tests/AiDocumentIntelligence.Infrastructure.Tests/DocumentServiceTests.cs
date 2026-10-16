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

    // Like the real repository, a document with no links yields an empty list rather than null.
    public DocumentServiceTests()
    {
        _claimRepositoryMock
            .Setup(r => r.GetAllClaimsForDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

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
    public async Task SaveDocumentAsync_WithClaimId_SavesDocumentTogetherWithItsLink()
    {
        var claimId = Guid.NewGuid();
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler, claimId);

        _repositoryMock.Verify(r => r.CreateDocumentAsync(document, claimId, TestUsers.Handler.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDocumentAsync_WithoutClaimId_LeavesDocumentUnmatched()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        _repositoryMock.Verify(r => r.CreateDocumentAsync(document, null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b" };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _claimRepositoryMock.Setup(r => r.GetAllClaimsForDocumentAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([claim]);
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
    public async Task DeleteDocumentAsync_AlsoLinkedToAnotherHandlersClaim_ReturnsForbidden()
    {
        var own = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var others = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.OtherHandler.Id };
        var document = new Document { Id = Guid.NewGuid(), BlobName = "b" };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _claimRepositoryMock.Setup(r => r.GetAllClaimsForDocumentAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([own, others]);
        var sut = CreateSut();

        var result = await sut.DeleteDocumentAsync(TestUsers.Handler, document.Id);

        Assert.Equal(AccessOutcome.Forbidden, result);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void DocumentVisibleTo(CurrentUser user, Document document) =>
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(user, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);

    private void ClaimVisibleTo(CurrentUser user, Claim claim) =>
        _claimRepositoryMock.Setup(r => r.GetClaimAsync(user, claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claim);

    [Fact]
    public async Task LinkToClaimAsync_ActableClaimAndVisibleDocument_LinksAndRecordsWhoDidIt()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Handler, document);
        ClaimVisibleTo(TestUsers.Handler, claim);
        var sut = CreateSut();

        var result = await sut.LinkToClaimAsync(TestUsers.Handler, document.Id, claim.Id);

        Assert.Equal(AccessOutcome.Allowed, result);
        _repositoryMock.Verify(r => r.LinkToClaimAsync(document.Id, claim.Id, TestUsers.Handler.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.UpsertAsync(It.Is<AppUser>(u => u.Id == TestUsers.Handler.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkToClaimAsync_AdminLinksDocumentToAnyClaim_IsAllowed()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Admin, document);
        ClaimVisibleTo(TestUsers.Admin, claim);
        var sut = CreateSut();

        Assert.Equal(AccessOutcome.Allowed, await sut.LinkToClaimAsync(TestUsers.Admin, document.Id, claim.Id));
    }

    [Fact]
    public async Task LinkToClaimAsync_DocumentNotVisible_ReturnsNotFoundAndLinksNothing()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        ClaimVisibleTo(TestUsers.Handler, claim);
        var sut = CreateSut();

        var result = await sut.LinkToClaimAsync(TestUsers.Handler, Guid.NewGuid(), claim.Id);

        Assert.Equal(AccessOutcome.NotFound, result);
        _repositoryMock.Verify(r => r.LinkToClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkToClaimAsync_ClaimNotVisible_ReturnsNotFound()
    {
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Handler, document);
        var sut = CreateSut();

        Assert.Equal(AccessOutcome.NotFound, await sut.LinkToClaimAsync(TestUsers.Handler, document.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task LinkToClaimAsync_ReaderWhoCanSeeBoth_ReturnsForbidden()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Reader, document);
        ClaimVisibleTo(TestUsers.Reader, claim);
        var sut = CreateSut();

        var result = await sut.LinkToClaimAsync(TestUsers.Reader, document.Id, claim.Id);

        Assert.Equal(AccessOutcome.Forbidden, result);
        _repositoryMock.Verify(r => r.LinkToClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkFromClaimAsync_ActableClaim_RemovesOnlyThatLink()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.Handler.Id };
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Handler, document);
        ClaimVisibleTo(TestUsers.Handler, claim);
        var sut = CreateSut();

        var result = await sut.UnlinkFromClaimAsync(TestUsers.Handler, document.Id, claim.Id);

        Assert.Equal(AccessOutcome.Allowed, result);
        _repositoryMock.Verify(r => r.UnlinkFromClaimAsync(document.Id, claim.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkFromClaimAsync_ClaimOfAnotherHandler_ReturnsForbidden()
    {
        var claim = new Claim { Id = Guid.NewGuid(), AssignedToUserId = TestUsers.OtherHandler.Id };
        var document = new Document { Id = Guid.NewGuid() };
        DocumentVisibleTo(TestUsers.Handler, document);
        ClaimVisibleTo(TestUsers.Handler, claim);
        var sut = CreateSut();

        Assert.Equal(AccessOutcome.Forbidden, await sut.UnlinkFromClaimAsync(TestUsers.Handler, document.Id, claim.Id));
        _repositoryMock.Verify(r => r.UnlinkFromClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDocumentsAsync_SearchTooLong_Throws()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetDocumentsAsync(TestUsers.Admin, search: new string('a', DocumentService.MaxSearchLength + 1)));
    }

    [Fact]
    public async Task GetDocumentsAsync_BlankSearch_MeansNoTextFilter()
    {
        var sut = CreateSut();

        await sut.GetDocumentsAsync(TestUsers.Admin, search: "   ", unmatchedOnly: true);

        _repositoryMock.Verify(r => r.GetDocumentsAsync(TestUsers.Admin, 1, 10, null, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLinkedClaimsAsync_DocumentNotVisible_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Null(await sut.GetLinkedClaimsAsync(TestUsers.Reader, Guid.NewGuid()));
        _claimRepositoryMock.Verify(r => r.GetClaimsForDocumentAsync(It.IsAny<CurrentUser>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
