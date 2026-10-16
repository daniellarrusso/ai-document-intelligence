using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class AIChatClaimMatchAssistantTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();

    private readonly Claim _jane = new() { Id = Guid.NewGuid(), Reference = "CLM-1", PolicyNumber = "P-1", ClaimantName = "Jane Doe" };
    private readonly Claim _john = new() { Id = Guid.NewGuid(), Reference = "CLM-2", PolicyNumber = "P-2", ClaimantName = "John Roe" };

    private AIChatClaimMatchAssistant CreateSut() => new(_chatClientMock.Object);

    private void Respond(string text) =>
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    [Fact]
    public async Task FindMatchesAsync_ModelNamesACandidate_ReturnsItWithTheReason()
    {
        Respond("""{"matches":[{"claim":2,"reason":"Letter is signed J. Roe"}]}""");

        var result = await CreateSut().FindMatchesAsync("text", [_jane, _john]);

        var match = Assert.Single(result);
        Assert.Same(_john, match.Claim);
        Assert.Equal(MatchMethod.Ai, match.Method);
        Assert.Equal("Letter is signed J. Roe", match.Detail);
    }

    [Fact]
    public async Task FindMatchesAsync_ModelWrapsJsonInProse_StillParses()
    {
        Respond("Sure! ```json\n{\"matches\":[{\"claim\":1,\"reason\":\"name\"}]}\n```");

        var result = await CreateSut().FindMatchesAsync("text", [_jane, _john]);

        Assert.Same(_jane, Assert.Single(result).Claim);
    }

    [Theory]
    [InlineData("""{"matches":[{"claim":3,"reason":"x"}]}""")]
    [InlineData("""{"matches":[{"claim":0,"reason":"x"}]}""")]
    [InlineData("""{"matches":[{"claim":"one","reason":"x"}]}""")]
    [InlineData("""{"matches":"none"}""")]
    [InlineData("""{"matches":[]}""")]
    [InlineData("no json at all")]
    [InlineData("{ not valid json }")]
    public async Task FindMatchesAsync_InvalidOrOutOfRangeAnswers_AreIgnored(string response)
    {
        Respond(response);

        var result = await CreateSut().FindMatchesAsync("text", [_jane, _john]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindMatchesAsync_SameCandidateTwice_IsReturnedOnce()
    {
        Respond("""{"matches":[{"claim":1,"reason":"a"},{"claim":1,"reason":"b"}]}""");

        var result = await CreateSut().FindMatchesAsync("text", [_jane]);

        Assert.Single(result);
    }

    [Fact]
    public async Task FindMatchesAsync_NoCandidates_DoesNotCallTheModel()
    {
        var result = await CreateSut().FindMatchesAsync("text", []);

        Assert.Empty(result);
        _chatClientMock.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindMatchesAsync_ModelFails_ThrowsInvalidOperationException()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().FindMatchesAsync("text", [_jane]));
    }

    [Fact]
    public async Task FindMatchesAsync_EmptyResponse_ThrowsInvalidOperationException()
    {
        Respond("  ");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().FindMatchesAsync("text", [_jane]));
    }

    [Fact]
    public async Task FindMatchesAsync_LongDocument_IsTruncatedInThePrompt()
    {
        IEnumerable<ChatMessage>? sent = null;
        _chatClientMock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => sent = m)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"matches":[]}""")));

        await CreateSut().FindMatchesAsync(new string('x', 50_000) + "TAIL", [_jane]);

        var prompt = sent!.Single().Text;
        Assert.DoesNotContain("TAIL", prompt);
        Assert.Contains("Jane Doe", prompt);
    }
}
