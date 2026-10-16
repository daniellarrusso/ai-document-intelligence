using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class ClaimTextMatcherTests
{
    private static Claim ClaimWith(string reference = "CLM-20261015-011076AB", string policy = "POL-778899", string name = "Jane Doe") =>
        new() { Id = Guid.NewGuid(), Reference = reference, PolicyNumber = policy, ClaimantName = name };

    [Fact]
    public void FindReferences_FindsDistinctUpperCasedReferences()
    {
        var text = "See clm-20261015-011076ab and CLM-20261015-011076AB, also CLM-20261016-FFFFFFFF.";

        Assert.Equal(["CLM-20261015-011076AB", "CLM-20261016-FFFFFFFF"], ClaimTextMatcher.FindReferences(text));
    }

    [Theory]
    [InlineData("CLM-2026101-011076AB")]
    [InlineData("CLM-20261015-0110")]
    [InlineData("nothing here")]
    public void FindReferences_MalformedOrAbsent_FindsNothing(string text)
    {
        Assert.Empty(ClaimTextMatcher.FindReferences(text));
    }

    [Fact]
    public void FindPolicyCandidates_KeepsTokensWithADigitAndDropsShortOnes()
    {
        var candidates = ClaimTextMatcher.FindPolicyCandidates("Policy pol-778899, ref A1, house number 12 and word.");

        Assert.Equal(["POL-778899"], candidates);
    }

    [Fact]
    public void Match_ReferenceInText_ReturnsReferenceEvidence()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(), "Re: claim clm-20261015-011076ab please.");

        Assert.Equal(MatchMethod.Reference, match!.Method);
    }

    [Fact]
    public void Match_ReferenceWinsOverPolicyAndName()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(), "CLM-20261015-011076AB POL-778899 Jane Doe");

        Assert.Equal(MatchMethod.Reference, match!.Method);
    }

    [Fact]
    public void Match_PolicyNumberOnly_ReturnsPolicyEvidence()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(), "Insured under pol-778899 since 2020.");

        Assert.Equal(MatchMethod.PolicyNumber, match!.Method);
    }

    [Fact]
    public void Match_PolicyNumberOnlyPartOfALongerToken_DoesNotMatch()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(), "Invoice POL-7788991 total due"));
    }

    [Fact]
    public void Match_ShortPolicyNumber_IsNotUsedAsEvidence()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(policy: "A1"), "Reference A1 appears here."));
    }

    [Theory]
    [InlineData("Statement from Doe, Jane regarding the loss.")]
    [InlineData("JANE DOE called on Monday.")]
    public void Match_FullNameInAnyOrderOrCase_ReturnsNameEvidence(string text)
    {
        var match = ClaimTextMatcher.Match(ClaimWith(), text);

        Assert.Equal(MatchMethod.ClaimantName, match!.Method);
    }

    [Fact]
    public void Match_NameWithSmallTypo_MatchesApproximately()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(name: "Katherine Johnson"), "Statement of Katherin Jonson.");

        Assert.Equal(MatchMethod.ClaimantName, match!.Method);
        Assert.Contains("close to", match.Detail);
    }

    [Fact]
    public void Match_OnlyOnePartOfTheNamePresent_DoesNotMatch()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(), "Jane Smith attended."));
    }

    [Fact]
    public void Match_SingleWordClaimantName_IsTooAmbiguous()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(name: "Madonna"), "Madonna sent this."));
    }

    [Fact]
    public void Match_ShortNamePartsGetNoTypoTolerance()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(name: "Jo Li"), "Joe Lee wrote this."));
    }

    [Fact]
    public void Match_UnrelatedText_ReturnsNull()
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(), "Quarterly sales figures for the northern region."));
    }

    [Theory]
    [InlineData("Hello, this is K. Johnson writing.")]
    [InlineData("signed k johnson")]
    public void Match_InitialAndExactSurname_ReturnsNameEvidence(string text)
    {
        var match = ClaimTextMatcher.Match(ClaimWith(name: "Katherine Johnson"), text);

        Assert.Equal(MatchMethod.ClaimantName, match!.Method);
        Assert.Contains("K. Johnson", match.Detail);
    }

    [Theory]
    [InlineData("This is M. Johnson writing.")]
    [InlineData("K. Johnston writing.")]
    [InlineData("Johnson alone, with no initial.")]
    public void Match_WrongInitialOrSurnameVariant_DoesNotMatch(string text)
    {
        Assert.Null(ClaimTextMatcher.Match(ClaimWith(name: "Katherine Johnson"), text));
    }

    // Extracted PDF text often has no line breaks, gluing a value to the next label.
    private const string GluedPoliceReport =
        "Metropolitan Police - Incident ReportClaim reference: CLM-20261015-011076ABPolicy number: 1234567Claimant: TestOn 14 October 2026";

    [Fact]
    public void Match_PolicyNumberGluedToTheNextWord_StillMatches()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(reference: "CLM-20261016-4E319990", policy: "1234567", name: "Test Adams"), GluedPoliceReport);

        Assert.Equal(MatchMethod.PolicyNumber, match!.Method);
    }

    [Fact]
    public void Match_ReferenceGluedToTheNextWord_StillMatches()
    {
        var match = ClaimTextMatcher.Match(ClaimWith(policy: "NOPE-1"), GluedPoliceReport);

        Assert.Equal(MatchMethod.Reference, match!.Method);
    }

    [Fact]
    public void FindPolicyCandidates_GluedToken_AlsoOffersThePieceBeforeTheWord()
    {
        Assert.Contains("1234567", ClaimTextMatcher.FindPolicyCandidates(GluedPoliceReport));
    }

    [Theory]
    [InlineData("Policy number1234567 attached")]
    [InlineData("(1234567)")]
    [InlineData("ref:1234567.")]
    public void ContainsIdentifier_StandaloneOrAcrossALetterDigitSwitch_IsFound(string text)
    {
        Assert.True(ClaimTextMatcher.ContainsIdentifier(text, "1234567"));
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("01234567")]
    [InlineData("Order 91234567 shipped")]
    public void ContainsIdentifier_InsideALongerNumber_IsNotFound(string text)
    {
        Assert.False(ClaimTextMatcher.ContainsIdentifier(text, "1234567"));
    }

    [Fact]
    public void ContainsIdentifier_LetterEdgeInsideALongerWord_IsNotFound()
    {
        Assert.False(ClaimTextMatcher.ContainsIdentifier("POL-ABCDE", "POL-ABCD"));
        Assert.True(ClaimTextMatcher.ContainsIdentifier("see pol-abcd.", "POL-ABCD"));
    }

    [Fact]
    public void ContainsIdentifier_FirstOccurrenceIsInsideALongerNumberButALaterOneIsStandalone_IsFound()
    {
        Assert.True(ClaimTextMatcher.ContainsIdentifier("id 12345678 and policy 1234567.", "1234567"));
    }
}
