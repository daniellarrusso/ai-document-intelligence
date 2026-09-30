using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class LocalDocumentProcessingWorkerTests
{
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IDocumentStorage> _storageMock = new();
    private readonly Mock<IDocumentTextExtractor> _extractorMock = new();
    private readonly Mock<IDocumentSummarizer> _summarizerMock = new();

    private static Document CreateDocument(Guid id) =>
        new() { Id = id, BlobName = "documents/x/original/file.pdf", ContentType = "application/pdf" };

    private LocalDocumentProcessingWorker CreateSut()
    {
        var services = new ServiceCollection()
            .AddSingleton(_repositoryMock.Object)
            .AddSingleton(_storageMock.Object)
            .AddSingleton(_extractorMock.Object)
            .AddSingleton(_summarizerMock.Object)
            .BuildServiceProvider();
        return new LocalDocumentProcessingWorker(
            new LocalDocumentProcessingQueue(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalDocumentProcessingWorker>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_Success_ProgressesThroughStatusesAndCompletesDocument()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), document.ContentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");
        _summarizerMock.Setup(s => s.SummarizeAsync("extracted text", It.IsAny<CancellationToken>())).ReturnsAsync("summary text");

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.ExtractingText, null, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveExtractedTextAsync(id, "extracted text", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateSummaryAsync(id, "summary text", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.MarkCompletedAsync(id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SummarizationFails_StillCompletesDocumentWithoutFailingIt()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), document.ContentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");
        _summarizerMock
            .Setup(s => s.SummarizeAsync("extracted text", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ollama is not running"));

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.SaveExtractedTextAsync(id, "extracted text", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateSummaryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkCompletedAsync(id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), DocumentStatus.Failed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DocumentNoLongerExists_DoesNothing()
    {
        var id = Guid.NewGuid();
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _storageMock.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveExtractedTextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DownloadFails_MarksDocumentFailed()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock
            .Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.Failed, It.IsNotNull<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveExtractedTextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ExtractionFails_MarksDocumentFailed()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), document.ContentType, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.ExtractingText, null, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.Failed, It.IsNotNull<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SaveExtractedTextFails_MarksDocumentFailed()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), document.ContentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");
        _repositoryMock
            .Setup(r => r.SaveExtractedTextAsync(id, "extracted text", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException());

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.Failed, It.IsNotNull<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateSummaryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SaveExtractedTextFailsAndFailureUpdateAlsoFails_DoesNotThrow()
    {
        var id = Guid.NewGuid();
        var document = CreateDocument(id);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        _storageMock.Setup(s => s.DownloadAsync(document.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), document.ContentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");
        _repositoryMock
            .Setup(r => r.SaveExtractedTextAsync(id, "extracted text", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException());
        _repositoryMock
            .Setup(r => r.UpdateStatusAsync(id, DocumentStatus.Failed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException());

        await CreateSut().ProcessAsync(id, CancellationToken.None);
    }

    [Fact]
    public async Task ProcessAsync_Cancelled_Throws()
    {
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetDocumentByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().ProcessAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_ProcessingAndFailureUpdateBothThrow_KeepsProcessingLaterDocuments()
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        var goodDocument = CreateDocument(good);
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(bad, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        _repositoryMock.Setup(r => r.GetDocumentByIdAsync(good, It.IsAny<CancellationToken>())).ReturnsAsync(goodDocument);
        _repositoryMock
            .Setup(r => r.UpdateStatusAsync(bad, It.IsAny<DocumentStatus>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        _storageMock.Setup(s => s.DownloadAsync(goodDocument.BlobName, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream());
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), goodDocument.ContentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");
        _summarizerMock.Setup(s => s.SummarizeAsync("extracted text", It.IsAny<CancellationToken>())).ReturnsAsync("summary text");

        var queue = new LocalDocumentProcessingQueue();
        var services = new ServiceCollection()
            .AddSingleton(_repositoryMock.Object)
            .AddSingleton(_storageMock.Object)
            .AddSingleton(_extractorMock.Object)
            .AddSingleton(_summarizerMock.Object)
            .BuildServiceProvider();
        var sut = new LocalDocumentProcessingWorker(
            queue, services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalDocumentProcessingWorker>.Instance);
        await queue.EnqueueAsync(bad);
        await queue.EnqueueAsync(good);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await sut.StopAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.MarkCompletedAsync(good, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_StoppedWhileIdle_StopsWithoutFaulting()
    {
        var sut = CreateSut();

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        Assert.False(sut.ExecuteTask?.IsFaulted, "Shutdown must not fault the worker (which stops the host).");
    }
}
