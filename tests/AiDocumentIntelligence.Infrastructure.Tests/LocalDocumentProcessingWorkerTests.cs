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
}
