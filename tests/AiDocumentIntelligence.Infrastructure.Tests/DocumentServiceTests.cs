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
    public async Task SaveDocumentAsync_StoresDetectedTypeAndSanitisedBlobName()
    {
        var sut = CreateSut();
        using var content = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]);
        string? blobName = null;
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), "image/jpeg", It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, name, _, _) => blobName = name)
            .ReturnsAsync("ignored");

        var document = await sut.SaveDocumentAsync(
            new UploadDocumentRequest(content, "../../evil name.JPG", "text/html", content.Length), TestUsers.Handler);

        Assert.Equal("image/jpeg", document.ContentType);
        Assert.Equal("../../evil name.JPG", document.FileName);
        Assert.Matches($@"^documents/{document.Id}/original/[0-9a-f]{{32}}\.jpg$", blobName);
    }

    [Fact]
    public async Task SaveDocumentAsync_ContentDoesNotMatchExtension_ThrowsInvalidFileExceptionAndStoresNothing()
    {
        var sut = CreateSut();
        using var content = new MemoryStream("MZ\x90\0"u8.ToArray());

        await Assert.ThrowsAsync<InvalidFileException>(() =>
            sut.SaveDocumentAsync(new UploadDocumentRequest(content, "invoice.pdf", "application/pdf", content.Length), TestUsers.Handler));

        _storageMock.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDocumentAsync_ValidFile_UploadsFromTheStartOfTheStream()
    {
        var sut = CreateSut();
        long? position = null;
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((stream, _, _, _) => position = stream.Position)
            .ReturnsAsync("ignored");

        await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(0, position);
    }

    [Fact]
    public async Task SaveDocumentAsync_RecordSaveFails_DeletesTheUploadedBlob()
    {
        _repositoryMock
            .Setup(r => r.CreateDocumentAsync(It.IsAny<Document>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler));

        _storageMock.Verify(s => s.DeleteAsync(It.Is<string>(n => n.StartsWith("documents/")), It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenContentAsync_DocumentNotVisible_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Null(await sut.OpenContentAsync(TestUsers.Handler, Guid.NewGuid()));
        _storageMock.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenContentAsync_VisibleDocument_ReturnsItsStoredBytes()
    {
        var document = new Document { Id = Guid.NewGuid(), BlobName = "documents/x/original/a.pdf" };
        _repositoryMock.Setup(r => r.GetDocumentForUserAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var stream = new MemoryStream([1]);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(stream);
        var sut = CreateSut();

        var result = await sut.OpenContentAsync(TestUsers.Handler, document.Id);

        Assert.Same(document, result!.Value.Document);
        Assert.Same(stream, result.Value.Content);
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

    [Fact]
    public async Task SaveDocumentAsync_StoresTheSha256OfTheUploadedBytes()
    {
        var sut = CreateSut();

        var document = await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(await ContentHasher.ComputeAsync(new MemoryStream([1, 2, 3])), document.ContentHash);
        _repositoryMock.Verify(r => r.CreateDocumentAsync(It.Is<Document>(d => d.ContentHash == document.ContentHash), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Starts with the PDF signature so the file type check passes; the last byte tells files apart.
    private static MemoryStream PdfStream(byte last) => new([.. "%PDF-1.7"u8.ToArray(), last]);

    [Fact]
    public async Task SaveDocumentAsync_IdenticalFilesUnderDifferentNames_GetTheSameHash()
    {
        var sut = CreateSut();

        var first = await sut.SaveDocumentAsync(new UploadDocumentRequest(PdfStream(9), "a.pdf", "application/pdf", 9), TestUsers.Handler);
        var second = await sut.SaveDocumentAsync(new UploadDocumentRequest(PdfStream(9), "copy of a.pdf", "application/pdf", 9), TestUsers.Handler);
        var different = await sut.SaveDocumentAsync(new UploadDocumentRequest(PdfStream(8), "b.pdf", "application/pdf", 9), TestUsers.Handler);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.ContentHash, different.ContentHash);
    }

    [Fact]
    public async Task SaveDocumentAsync_StorageStillReceivesTheWholeFileFromTheStart()
    {
        long? positionAtUpload = null;
        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((stream, _, _, _) => positionAtUpload = stream.Position)
            .ReturnsAsync("blob");
        var sut = CreateSut();

        await sut.SaveDocumentAsync(CreateRequest(), TestUsers.Handler);

        Assert.Equal(0, positionAtUpload);
    }

    [Fact]
    public async Task SaveDocumentAsync_UnhashableStream_FailsBeforeStoringAnything()
    {
        var sut = CreateSut();
        var request = new UploadDocumentRequest(new ForwardOnlyStream(), "a.pdf", "application/pdf", 1);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SaveDocumentAsync(request, TestUsers.Handler));

        _storageMock.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateDocumentAsync(It.IsAny<Document>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class ForwardOnlyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        // Yields the PDF signature once so the file type check passes and the hashing step is what fails.
        private bool _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read) return 0;
            _read = true;
            "%PDF-1.7"u8.CopyTo(buffer.AsSpan(offset));
            return 8;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
