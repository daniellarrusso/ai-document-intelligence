namespace AiDocumentIntelligence.Infrastructure;

// Splits text into overlapping, roughly fixed-size chunks, preferring to break at line or sentence
// boundaries so a clause (e.g. a policy exclusion) is less likely to be cut in half.
public static class TextChunker
{
    public const int DefaultMaxChunkLength = 1000;
    public const int DefaultOverlap = 150;

    public static IReadOnlyList<string> Split(string text, int maxChunkLength = DefaultMaxChunkLength, int overlap = DefaultOverlap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkLength, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        if (overlap >= maxChunkLength)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be smaller than the maximum chunk length.");
        }

        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + maxChunkLength, text.Length);
            if (end < text.Length)
            {
                end = FindBreak(text, start, end);
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            if (end >= text.Length)
            {
                break;
            }

            // Step back by the overlap, but always make forward progress.
            start = Math.Max(end - overlap, start + 1);

            // Don't start mid-word.
            while (start < end && !char.IsWhiteSpace(text[start - 1]))
            {
                start++;
            }
        }

        return chunks;
    }

    // Returns the exclusive end of the best chunk in [start, limit): the last line/sentence end in the
    // second half of the window, else the last whitespace, else the hard limit.
    private static int FindBreak(string text, int start, int limit)
    {
        var minimum = start + (limit - start) / 2;
        var lastWhitespace = -1;

        for (var i = limit - 1; i >= minimum; i--)
        {
            var c = text[i];
            if (c == '\n')
            {
                return i + 1;
            }

            if (c is ('.' or '!' or '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }

            if (lastWhitespace < 0 && char.IsWhiteSpace(c))
            {
                lastWhitespace = i + 1;
            }
        }

        return lastWhitespace > 0 ? lastWhitespace : limit;
    }
}
