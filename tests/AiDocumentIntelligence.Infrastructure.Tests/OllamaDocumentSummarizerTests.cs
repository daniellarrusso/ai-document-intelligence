using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class OllamaDocumentSummarizerTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();

    private OllamaDocumentSummarizer CreateSut() => new(_chatClientMock.Object);

    private void SetupResponse(string text)
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SummarizeAsync_InvalidText_ThrowsArgumentException(string? text)
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.SummarizeAsync(text!));

        Assert.Equal("text", ex.ParamName);
    }

    [Fact]
    public async Task SummarizeAsync_ValidText_ReturnsSummaryFromResponse()
    {
        SetupResponse("A concise summary.");
        var sut = CreateSut();

        var result = await sut.SummarizeAsync("Some long document text.");

        Assert.Equal("A concise summary.", result);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyResponseText_ThrowsInvalidOperationException()
    {
        SetupResponse(string.Empty);
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SummarizeAsync("text"));
    }

    [Fact]
    public async Task SummarizeAsync_ChatClientThrows_WrapsInInvalidOperationException()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SummarizeAsync("text"));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task SummarizeAsync_LongText_TruncatesTo8000Characters()
    {
        ChatMessage? capturedMessage = null;
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => capturedMessage = messages.Single())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var sut = CreateSut();
        var longText = new string('a', 10_000);

        await sut.SummarizeAsync(longText);

        Assert.NotNull(capturedMessage);
        Assert.Contains(new string('a', 8000), capturedMessage!.Text);
        Assert.DoesNotContain(new string('a', 8001), capturedMessage.Text);
    }
}
