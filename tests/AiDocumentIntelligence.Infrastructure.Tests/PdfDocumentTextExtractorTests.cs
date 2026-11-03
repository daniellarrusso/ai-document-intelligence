using System.Text;
using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class PdfDocumentTextExtractorTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");

    private static PdfDocumentTextExtractor CreateSut() => new();

    [Fact]
    public async Task ExtractTextAsync_NullContent_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ExtractTextAsync(null!, "application/pdf"));

        Assert.Equal("content", ex.ParamName);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyContent_ThrowsArgumentException()
    {
        var sut = CreateSut();
        using var content = new MemoryStream();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ExtractTextAsync(content, "application/pdf"));

        Assert.Equal("content", ex.ParamName);
    }

    [Fact]
    public async Task ExtractTextAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.ExtractTextAsync(content, "image/png"));
    }

    [Fact]
    public async Task ExtractTextAsync_MalformedPdf_WrapsInInvalidOperationException()
    {
        var sut = CreateSut();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("this is not a pdf"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExtractTextAsync(content, "application/pdf"));
    }

    [Fact]
    public async Task ExtractTextAsync_ValidPdf_ReturnsExtractedText()
    {
        var sut = CreateSut();
        await using var content = File.OpenRead(FixturePath);

        var result = await sut.ExtractTextAsync(content, "application/pdf");

        Assert.Contains("Hello World", result);
    }

    private static readonly string PoliceReportPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "police-report.pdf");

    [Fact]
    public async Task ExtractTextAsync_SeparateLines_KeepsTheLineBreaks()
    {
        var sut = CreateSut();
        await using var content = File.OpenRead(PoliceReportPath);

        var result = await sut.ExtractTextAsync(content, "application/pdf");

        // Raw page text gave "Policy number: 1234567Claimant: TestOn 14 October...".
        Assert.Contains("Policy number: 1234567\n", result.Replace("\r\n", "\n"));
        Assert.Contains("Claimant: Test\n", result.Replace("\r\n", "\n"));
        Assert.DoesNotContain("1234567Claimant", result);
    }

    [Fact]
    public async Task ExtractTextAsync_ThenMatching_FindsTheClaimByPolicyNumberAndName()
    {
        var sut = CreateSut();
        await using var content = File.OpenRead(PoliceReportPath);
        var text = await sut.ExtractTextAsync(content, "application/pdf");
        var claim = new Claim { Id = Guid.NewGuid(), Reference = "CLM-20261016-4E319990", PolicyNumber = "1234567", ClaimantName = "Test Adams" };

        Assert.Equal(MatchMethod.PolicyNumber, ClaimTextMatcher.Match(claim, text)!.Method);
        Assert.Contains("1234567", ClaimTextMatcher.FindPolicyCandidates(text));
        Assert.Contains("CLM-20261015-011076AB", ClaimTextMatcher.FindReferences(text));
    }
}
