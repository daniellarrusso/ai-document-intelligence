using System.Text;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class PlainTextDocumentTextExtractorTests
{
    private readonly PlainTextDocumentTextExtractor _sut = new();

    [Fact]
    public async Task ExtractTextAsync_Utf8Text_ReturnsTrimmedText()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("  héllo\nworld \n"));

        Assert.Equal("héllo\nworld", await _sut.ExtractTextAsync(content, "text/plain"));
    }

    [Fact]
    public async Task ExtractTextAsync_OtherContentType_ThrowsNotSupported()
    {
        using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<NotSupportedException>(() => _sut.ExtractTextAsync(content, "application/pdf"));
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyContent_ThrowsArgumentException()
    {
        using var content = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ExtractTextAsync(content, "text/plain"));
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("TEXT/PLAIN", true)]
    [InlineData("image/png", false)]
    public void CanHandle_ReportsOnlyPlainText(string contentType, bool expected)
    {
        Assert.Equal(expected, _sut.CanHandle(contentType));
    }
}
