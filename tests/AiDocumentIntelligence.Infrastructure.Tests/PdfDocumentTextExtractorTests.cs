using System.Text;
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
}
