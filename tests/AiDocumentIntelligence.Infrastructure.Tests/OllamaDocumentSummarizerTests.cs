using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class OllamaDocumentSummarizerTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock = new();

    private OllamaDocumentSummarizer CreateSut()
    {
        var httpClient = new HttpClient(_handlerMock.Object) { BaseAddress = new Uri("http://localhost:11434") };
        var options = Options.Create(new OllamaOptions { BaseUrl = "http://localhost:11434", Model = "llama3.2" });
        return new OllamaDocumentSummarizer(httpClient, options);
    }

    private void SetupResponse(HttpStatusCode statusCode, string content)
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
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
        SetupResponse(HttpStatusCode.OK, """{"response":"A concise summary.","done":true}""");
        var sut = CreateSut();

        var result = await sut.SummarizeAsync("Some long document text.");

        Assert.Equal("A concise summary.", result);
    }

    [Fact]
    public async Task SummarizeAsync_NonSuccessStatusCode_ThrowsInvalidOperationException()
    {
        SetupResponse(HttpStatusCode.InternalServerError, "");
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SummarizeAsync("text"));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyResponseField_ThrowsInvalidOperationException()
    {
        SetupResponse(HttpStatusCode.OK, """{"response":null,"done":true}""");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SummarizeAsync("text"));
    }

    [Fact]
    public async Task SummarizeAsync_HttpClientThrows_WrapsInInvalidOperationException()
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SummarizeAsync("text"));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
