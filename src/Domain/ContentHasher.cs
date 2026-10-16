using System.Security.Cryptography;

namespace AiDocumentIntelligence.Domain;

// Fingerprints a file's bytes so identical files can be recognised later. SHA-256 gives no practical
// false positives, which is what lets an exact-copy check be trusted.
public static class ContentHasher
{
    // Lowercase hexadecimal SHA-256 is always this many characters.
    public const int HexLength = 64;

    // Hashes everything from the start of the stream, then puts the position back where it was so the caller
    // can still read the content (for example to upload it). The stream must be seekable.
    public static async Task<string> ComputeAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
        {
            throw new ArgumentException("The content must be seekable so it can be read again after hashing.", nameof(content));
        }

        var originalPosition = content.Position;
        content.Position = 0;
        try
        {
            var hash = await SHA256.HashDataAsync(content, cancellationToken);

            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            content.Position = originalPosition;
        }
    }
}
