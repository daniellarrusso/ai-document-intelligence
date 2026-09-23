using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class LocalDocumentProcessingWorkerTests
{
    private readonly Mock<IDocumentRepository> _repositoryMock = new();

    private LocalDocumentProcessingWorker CreateSut()
    {
        var services = new ServiceCollection().AddSingleton(_repositoryMock.Object).BuildServiceProvider();
        return new LocalDocumentProcessingWorker(
            new LocalDocumentProcessingQueue(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalDocumentProcessingWorker>.Instance,
            TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task ProcessAsync_Success_MarksDocumentCompleted()
    {
        var id = Guid.NewGuid();

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.Completed, It.IsNotNull<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UpdateFails_MarksDocumentFailed()
    {
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.UpdateStatusAsync(id, DocumentStatus.Completed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        await CreateSut().ProcessAsync(id, CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(id, DocumentStatus.Failed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().ProcessAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_ProcessingAndFailureUpdateBothThrow_KeepsProcessingLaterDocuments()
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.UpdateStatusAsync(bad, It.IsAny<DocumentStatus>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var queue = new LocalDocumentProcessingQueue();
        var services = new ServiceCollection().AddSingleton(_repositoryMock.Object).BuildServiceProvider();
        var sut = new LocalDocumentProcessingWorker(
            queue, services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalDocumentProcessingWorker>.Instance, TimeSpan.FromMilliseconds(10));
        await queue.EnqueueAsync(bad);
        await queue.EnqueueAsync(good);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await sut.StopAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(good, DocumentStatus.Completed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
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
