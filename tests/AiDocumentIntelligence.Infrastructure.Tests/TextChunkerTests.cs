using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class TextChunkerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Split_BlankText_ReturnsNoChunks(string? text)
    {
        Assert.Empty(TextChunker.Split(text!));
    }

    [Fact]
    public void Split_TextShorterThanMax_ReturnsSingleTrimmedChunk()
    {
        var chunks = TextChunker.Split("  Short policy text.  ");

        Assert.Equal(["Short policy text."], chunks);
    }

    [Fact]
    public void Split_LongText_NeverExceedsMaxChunkLength()
    {
        var text = string.Join(' ', Enumerable.Repeat("Water damage is excluded unless sudden and accidental.", 100));

        var chunks = TextChunker.Split(text, maxChunkLength: 200, overlap: 40);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 200));
    }

    [Fact]
    public void Split_LongText_PrefersSentenceBoundaries()
    {
        var text = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"Sentence number {i} ends here."));

        var chunks = TextChunker.Split(text, maxChunkLength: 200, overlap: 40);

        Assert.All(chunks, c => Assert.EndsWith(".", c));
    }

    [Fact]
    public void Split_LongText_ConsecutiveChunksOverlap()
    {
        var text = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"Sentence number {i} ends here."));

        var chunks = TextChunker.Split(text, maxChunkLength: 200, overlap: 60);

        for (var i = 1; i < chunks.Count; i++)
        {
            var firstWordOfNext = chunks[i].Split(' ')[0];
            Assert.Contains(firstWordOfNext, chunks[i - 1]);
        }
    }

    [Fact]
    public void Split_LongText_CoversAllContent()
    {
        var words = Enumerable.Range(1, 500).Select(i => $"w{i}").ToList();

        var chunks = TextChunker.Split(string.Join(' ', words), maxChunkLength: 100, overlap: 20);

        var covered = chunks.SelectMany(c => c.Split(' ')).ToHashSet();
        Assert.All(words, w => Assert.Contains(w, covered));
    }

    [Fact]
    public void Split_TextWithoutWhitespace_FallsBackToHardSplitsAndTerminates()
    {
        var chunks = TextChunker.Split(new string('x', 1000), maxChunkLength: 100, overlap: 10);

        Assert.True(chunks.Count >= 10);
        Assert.All(chunks, c => Assert.True(c.Length <= 100));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(100, 150)]
    [InlineData(100, -1)]
    public void Split_InvalidSizes_ThrowsArgumentOutOfRangeException(int max, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("text", max, overlap));
    }
}
