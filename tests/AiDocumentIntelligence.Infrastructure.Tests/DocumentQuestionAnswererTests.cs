using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class DocumentQuestionAnswererTests
{
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingMock = new();
    private readonly Mock<IDocumentChunkRepository> _chunkRepositoryMock = new();
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly float[] _queryVector = new float[EmbeddingDefaults.Dimensions];

    public DocumentQuestionAnswererTests()
    {
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(_queryVector)]));
    }

    private DocumentQuestionAnswerer CreateSut() => new(_embeddingMock.Object, _chunkRepositoryMock.Object, _chatClientMock.Object);

    private void SetupMatches(params DocumentChunkMatch[] matches)
    {
        _chunkRepositoryMock
            .Setup(r => r.SearchAsync(_documentId, It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);
    }

    private List<ChatMessage> SetupChatResponse(string text)
    {
        var sent = new List<ChatMessage>();
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) => sent.AddRange(messages))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return sent;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskAsync_BlankQuestion_ThrowsArgumentException(string? question)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateSut().AskAsync(_documentId, question!));

        Assert.Equal("question", ex.ParamName);
    }

    [Fact]
    public async Task AskAsync_QuestionTooLong_ThrowsArgumentException()
    {
        var question = new string('a', DocumentQuestionAnswerer.MaxQuestionLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateSut().AskAsync(_documentId, question));
    }

    [Fact]
    public async Task AskAsync_NoIndexedChunks_ReturnsNullWithoutCallingTheModel()
    {
        SetupMatches();

        var result = await CreateSut().AskAsync(_documentId, "What is excluded?");

        Assert.Null(result);
        _chatClientMock.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AskAsync_WithMatches_ReturnsAnswerAndSourcesFromRetrievedChunks()
    {
        var matches = new[]
        {
            new DocumentChunkMatch(4, "Water damage from flooding is excluded.", 0.91),
            new DocumentChunkMatch(9, "Gradual leaks are not covered.", 0.84),
        };
        SetupMatches(matches);
        SetupChatResponse("  Flooding and gradual leaks are excluded [1][2].  ");

        var result = await CreateSut().AskAsync(_documentId, "What are the exclusions for water damage?");

        Assert.NotNull(result);
        Assert.Equal("Flooding and gradual leaks are excluded [1][2].", result.Answer);
        Assert.Equal(matches, result.Sources);
    }

    [Fact]
    public async Task AskAsync_WithMatches_EmbedsQuestionSearchesOnlyThisDocumentAndGroundsThePrompt()
    {
        SetupMatches(
            new DocumentChunkMatch(4, "Water damage from flooding is excluded.", 0.91),
            new DocumentChunkMatch(9, "Gradual leaks are not covered.", 0.84));
        var sent = SetupChatResponse("answer");

        await CreateSut().AskAsync(_documentId, "What are the exclusions for water damage?");

        _embeddingMock.Verify(
            e => e.GenerateAsync(It.Is<IEnumerable<string>>(v => v.Single() == "What are the exclusions for water damage?"), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepositoryMock.Verify(
            r => r.SearchAsync(_documentId, It.Is<ReadOnlyMemory<float>>(v => v.Length == _queryVector.Length), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Contains("only the excerpts", sent[0].Text);
        var user = sent[1].Text;
        Assert.Contains("[1] Water damage from flooding is excluded.", user);
        Assert.Contains("[2] Gradual leaks are not covered.", user);
        Assert.Contains("Question: What are the exclusions for water damage?", user);
    }

    [Fact]
    public async Task AskAsync_EmptyModelResponse_ThrowsInvalidOperationException()
    {
        SetupMatches(new DocumentChunkMatch(0, "text", 0.9));
        SetupChatResponse(string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().AskAsync(_documentId, "Question?"));
    }

    [Fact]
    public async Task AskAsync_ChatClientThrows_WrapsInInvalidOperationException()
    {
        var inner = new HttpRequestException("Connection refused");
        SetupMatches(new DocumentChunkMatch(0, "text", 0.9));
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().AskAsync(_documentId, "Question?"));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task AskAsync_EmbeddingGeneratorThrows_WrapsInInvalidOperationException()
    {
        var inner = new HttpRequestException("Connection refused");
        _embeddingMock
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().AskAsync(_documentId, "Question?"));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task AskAsync_Cancelled_PropagatesWithoutWrapping()
    {
        _chunkRepositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<Guid>(), It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().AskAsync(_documentId, "Question?"));
    }
}
