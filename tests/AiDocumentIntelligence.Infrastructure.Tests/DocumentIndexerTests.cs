using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class DocumentIndexerTests
{
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingMock = new();
    private readonly Mock<IDocumentChunkRepository> _chunkRepositoryMock = new();

    private DocumentIndexer CreateSut() => new(_embeddingMock.Object, _chunkRepositoryMock.Object);

    private static Embedding<float> Embed(int dimensions = EmbeddingDefaults.Dimensions) => new(new float[dimensions]);

    // Returns one embedding per input, so tests don't depend on how the indexer batches.
    private void SetupEmbeddings(int dimensions = EmbeddingDefaults.Dimensions)
    {
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken _) =>
                new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => Embed(dimensions)).ToList()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IndexAsync_InvalidText_ThrowsArgumentException(string? text)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateSut().IndexAsync(Guid.NewGuid(), text!));

        Assert.Equal("text", ex.ParamName);
    }

    [Fact]
    public async Task IndexAsync_ValidText_StoresOrderedChunksForTheDocument()
    {
        SetupEmbeddings();
        var id = Guid.NewGuid();
        var text = string.Join(' ', Enumerable.Repeat("Water damage is excluded unless sudden and accidental.", 60));
        IReadOnlyList<DocumentChunk>? stored = null;
        _chunkRepositoryMock
            .Setup(r => r.ReplaceChunksAsync(id, It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyList<DocumentChunk> chunks, CancellationToken _) => stored = chunks)
            .Returns(Task.CompletedTask);

        await CreateSut().IndexAsync(id, text);

        Assert.NotNull(stored);
        Assert.True(stored.Count > 1);
        Assert.All(stored, c => Assert.Equal(id, c.DocumentId));
        Assert.Equal(Enumerable.Range(0, stored.Count), stored.Select(c => c.ChunkIndex));
        Assert.Equal(TextChunker.Split(text), stored.Select(c => c.Text));
    }

    [Fact]
    public async Task IndexAsync_ManyChunks_EmbedsInBatchesAndKeepsGlobalChunkIndexes()
    {
        SetupEmbeddings();
        var text = string.Join(' ', Enumerable.Range(1, 6000).Select(i => $"word{i}"));
        var expectedChunks = TextChunker.Split(text).Count;
        Assert.True(expectedChunks > 32);
        IReadOnlyList<DocumentChunk>? stored = null;
        _chunkRepositoryMock
            .Setup(r => r.ReplaceChunksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyList<DocumentChunk> chunks, CancellationToken _) => stored = chunks)
            .Returns(Task.CompletedTask);

        await CreateSut().IndexAsync(Guid.NewGuid(), text);

        _embeddingMock.Verify(
            e => e.GenerateAsync(It.Is<IEnumerable<string>>(v => v.Count() <= 32), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()),
            Times.Exactly((int)Math.Ceiling(expectedChunks / 32d)));
        Assert.Equal(Enumerable.Range(0, expectedChunks), stored!.Select(c => c.ChunkIndex));
    }

    [Fact]
    public async Task IndexAsync_WrongEmbeddingDimensions_ThrowsAndStoresNothing()
    {
        SetupEmbeddings(dimensions: 384);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().IndexAsync(Guid.NewGuid(), "Some text."));

        Assert.Contains("384", ex.Message);
        _chunkRepositoryMock.Verify(
            r => r.ReplaceChunksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexAsync_EmbeddingCountMismatch_ThrowsAndStoresNothing()
    {
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().IndexAsync(Guid.NewGuid(), "Some text."));

        _chunkRepositoryMock.Verify(
            r => r.ReplaceChunksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexAsync_EmbeddingGeneratorThrows_WrapsInInvalidOperationException()
    {
        var inner = new HttpRequestException("Connection refused");
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().IndexAsync(Guid.NewGuid(), "Some text."));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task IndexAsync_RepositoryThrows_WrapsInInvalidOperationException()
    {
        SetupEmbeddings();
        var inner = new InvalidCastException("db down");
        _chunkRepositoryMock
            .Setup(r => r.ReplaceChunksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().IndexAsync(Guid.NewGuid(), "Some text."));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task IndexAsync_Cancelled_PropagatesWithoutWrapping()
    {
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().IndexAsync(Guid.NewGuid(), "Some text."));
    }
}
