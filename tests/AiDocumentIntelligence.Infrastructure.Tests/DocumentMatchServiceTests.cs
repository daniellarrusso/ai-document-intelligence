using AiDocumentIntelligence.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class DocumentMatchServiceTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IClaimRepository> _claims = new();
    private readonly Mock<IClaimMatchAssistant> _assistant = new();

    private readonly Claim _jane = new() { Id = Guid.NewGuid(), Reference = "CLM-20261015-011076AB", PolicyNumber = "POL-778899", ClaimantName = "Jane Doe", AssignedToUserId = TestUsers.Handler.Id, CreatedAt = DateTime.UtcNow };
    private readonly Claim _john = new() { Id = Guid.NewGuid(), Reference = "CLM-20261015-0000AAAA", PolicyNumber = "POL-111222", ClaimantName = "John Roe", AssignedToUserId = TestUsers.Handler.Id, CreatedAt = DateTime.UtcNow.AddDays(-1) };

    public DocumentMatchServiceTests()
    {
        // Like the real repositories, "nothing" is an empty list rather than null.
        _claims.Setup(r => r.GetClaimsForDocumentAsync(It.IsAny<CurrentUser>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _claims.Setup(r => r.GetClaimsForMatchingAsync(It.IsAny<CurrentUser>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([_jane, _john]);
        _claims.Setup(r => r.GetClaimsByIdentifiersAsync(It.IsAny<CurrentUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private DocumentMatchService CreateSut() =>
        new(_documents.Object, _claims.Object, _assistant.Object, NullLogger<DocumentMatchService>.Instance);

    private Document DocumentWithText(CurrentUser user, string? text)
    {
        var document = new Document { Id = Guid.NewGuid(), ExtractedText = text };
        _documents.Setup(r => r.GetDocumentForUserAsync(user, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        return document;
    }

    [Fact]
    public async Task ScanAsync_DocumentNotVisible_ReturnsNull()
    {
        Assert.Null(await CreateSut().ScanAsync(TestUsers.Handler, Guid.NewGuid(), useAi: false));
    }

    [Fact]
    public async Task ScanAsync_NoExtractedText_ReportsTextUnavailable()
    {
        var document = DocumentWithText(TestUsers.Handler, null);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.False(scan!.TextAvailable);
        Assert.Empty(scan.Suggestions);
        _assistant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScanAsync_TextQuotesAReference_SuggestsThatClaim()
    {
        var document = DocumentWithText(TestUsers.Handler, "Regarding claim CLM-20261015-011076AB.");

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        var suggestion = Assert.Single(scan!.Suggestions);
        Assert.Same(_jane, suggestion.Claim);
        Assert.Equal(MatchMethod.Reference, suggestion.Method);
    }

    [Fact]
    public async Task ScanAsync_ReferenceOutsideTheNewestClaims_IsFoundByIdentifierLookup()
    {
        var old = new Claim { Id = Guid.NewGuid(), Reference = "CLM-20200101-DEADBEEF", PolicyNumber = "X", ClaimantName = "Old One", AssignedToUserId = TestUsers.Handler.Id };
        _claims.Setup(r => r.GetClaimsByIdentifiersAsync(TestUsers.Handler, It.Is<IReadOnlyCollection<string>>(refs => refs.Contains("CLM-20200101-DEADBEEF")), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([old]);
        var document = DocumentWithText(TestUsers.Handler, "See CLM-20200101-DEADBEEF");

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        Assert.Same(old, Assert.Single(scan!.Suggestions).Claim);
    }

    [Fact]
    public async Task ScanAsync_AlreadyLinkedClaim_IsNotSuggestedAgain()
    {
        var document = DocumentWithText(TestUsers.Handler, "CLM-20261015-011076AB");
        _claims.Setup(r => r.GetClaimsForDocumentAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_jane]);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        Assert.Empty(scan!.Suggestions);
        var check = Assert.Single(scan.LinkedClaims);
        Assert.NotNull(check.Evidence);
    }

    [Fact]
    public async Task ScanAsync_LinkedClaimNotMentionedInText_IsFlaggedUnsupported()
    {
        var document = DocumentWithText(TestUsers.Handler, "An unrelated invoice for office chairs.");
        _claims.Setup(r => r.GetClaimsForDocumentAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_john]);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        Assert.Null(Assert.Single(scan!.LinkedClaims).Evidence);
    }

    [Fact]
    public async Task ScanAsync_ClaimTheUserCannotActOn_IsNotSuggested()
    {
        var elsewhere = new Claim { Id = Guid.NewGuid(), Reference = "CLM-20261015-BBBBBBBB", PolicyNumber = "P-9", ClaimantName = "Jane Doe", AssignedToUserId = TestUsers.OtherHandler.Id };
        _claims.Setup(r => r.GetClaimsForMatchingAsync(TestUsers.Reader, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([elsewhere]);
        var document = DocumentWithText(TestUsers.Reader, "CLM-20261015-BBBBBBBB");

        var scan = await CreateSut().ScanAsync(TestUsers.Reader, document.Id, useAi: false);

        Assert.Empty(scan!.Suggestions);
    }

    [Fact]
    public async Task ScanAsync_StrongerEvidenceComesFirst()
    {
        var document = DocumentWithText(TestUsers.Handler, "Jane Doe wrote about POL-111222.");

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        Assert.Equal([MatchMethod.PolicyNumber, MatchMethod.ClaimantName], scan!.Suggestions.Select(s => s.Method));
    }

    [Fact]
    public async Task ScanAsync_NothingFoundAndAiNotRequested_DoesNotCallTheAssistant()
    {
        var document = DocumentWithText(TestUsers.Handler, "Totally unrelated text.");

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: false);

        Assert.Empty(scan!.Suggestions);
        Assert.False(scan.AiAttempted);
        _assistant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScanAsync_NothingFoundAndAiRequested_UsesTheAssistantsSuggestions()
    {
        var document = DocumentWithText(TestUsers.Handler, "Letter from Mr Roe's solicitor about the loss.");
        _assistant
            .Setup(a => a.FindMatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Claim>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClaimMatch(_john, MatchMethod.Ai, "Signed J. Roe")]);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.True(scan!.AiAttempted);
        Assert.False(scan.AiFailed);
        Assert.Same(_john, Assert.Single(scan.Suggestions).Claim);
    }

    [Fact]
    public async Task ScanAsync_DeterministicMatchFound_SkipsTheAssistantEvenIfRequested()
    {
        var document = DocumentWithText(TestUsers.Handler, "CLM-20261015-011076AB");

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.False(scan!.AiAttempted);
        _assistant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScanAsync_AssistantFails_ReturnsEmptySuggestionsAndFlagsTheFailure()
    {
        var document = DocumentWithText(TestUsers.Handler, "Nothing recognisable.");
        _assistant
            .Setup(a => a.FindMatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Claim>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.True(scan!.AiFailed);
        Assert.Empty(scan.Suggestions);
    }

    [Fact]
    public async Task ScanAsync_AssistantIsOnlyShownClaimsTheUserCanActOnAndHasNotLinked()
    {
        var document = DocumentWithText(TestUsers.Handler, "Nothing recognisable.");
        var others = new Claim { Id = Guid.NewGuid(), Reference = "CLM-20261015-CCCCCCCC", PolicyNumber = "P-3", ClaimantName = "Other Person", AssignedToUserId = TestUsers.OtherHandler.Id };
        _claims.Setup(r => r.GetClaimsForMatchingAsync(TestUsers.Handler, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([_jane, _john, others]);
        _claims.Setup(r => r.GetClaimsForDocumentAsync(TestUsers.Handler, document.Id, It.IsAny<CancellationToken>())).ReturnsAsync([_jane]);
        IReadOnlyList<Claim>? shown = null;
        _assistant
            .Setup(a => a.FindMatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Claim>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Claim>, CancellationToken>((_, c, _) => shown = c)
            .ReturnsAsync([]);

        await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.Equal([_john], shown);
    }

    [Fact]
    public async Task ScanAsync_AssistantNamesAClaimTheTextNeverMentions_AnswerIsDropped()
    {
        var document = DocumentWithText(TestUsers.Handler, "Hello from someone about a car.");
        _assistant
            .Setup(a => a.FindMatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Claim>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClaimMatch(_john, MatchMethod.Ai, "Made up")]);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.True(scan!.AiAttempted);
        Assert.Empty(scan.Suggestions);
    }

    [Fact]
    public async Task ScanAsync_AssistantAnswerBackedByAPartOfTheName_IsKept()
    {
        var document = DocumentWithText(TestUsers.Handler, "Please contact Roe about the loss.");
        _assistant
            .Setup(a => a.FindMatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Claim>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClaimMatch(_john, MatchMethod.Ai, "Surname Roe")]);

        var scan = await CreateSut().ScanAsync(TestUsers.Handler, document.Id, useAi: true);

        Assert.Same(_john, Assert.Single(scan!.Suggestions).Claim);
    }
}
