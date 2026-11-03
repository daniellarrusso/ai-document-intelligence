namespace AiDocumentIntelligence.Domain;

// The single allow-list of uploadable file types. A file is accepted only when its extension is listed AND its
// leading bytes match that type, and the content type we store is the one detected here, never the client's.
public static class FileTypePolicy
{
    public const long MaxDocumentBytes = 50L * 1024 * 1024;
    public const long MaxImageBytes = 25L * 1024 * 1024;
    public const long MaxVideoBytes = 500L * 1024 * 1024;

    // Enough leading bytes to recognise every signature below.
    public const int HeaderLength = 16;

    private sealed record FileType(string ContentType, FileCategory Category, Func<byte[], bool> Matches);

    private static readonly Dictionary<string, FileType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new("application/pdf", FileCategory.Document, h => StartsWith(h, "%PDF-"u8)),
        [".txt"] = new("text/plain", FileCategory.Document, h => Array.IndexOf(h, (byte)0) < 0),
        [".jpg"] = new("image/jpeg", FileCategory.Image, h => StartsWith(h, [0xFF, 0xD8, 0xFF])),
        [".jpeg"] = new("image/jpeg", FileCategory.Image, h => StartsWith(h, [0xFF, 0xD8, 0xFF])),
        [".png"] = new("image/png", FileCategory.Image, h => StartsWith(h, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        [".gif"] = new("image/gif", FileCategory.Image, h => StartsWith(h, "GIF87a"u8) || StartsWith(h, "GIF89a"u8)),
        [".webp"] = new("image/webp", FileCategory.Image, h => StartsWith(h, "RIFF"u8) && At(h, 8, "WEBP"u8)),
        [".mp4"] = new("video/mp4", FileCategory.Video, h => At(h, 4, "ftyp"u8)),
        [".mov"] = new("video/quicktime", FileCategory.Video, h => At(h, 4, "ftyp"u8) || At(h, 4, "moov"u8) || At(h, 4, "mdat"u8) || At(h, 4, "wide"u8)),
        [".webm"] = new("video/webm", FileCategory.Video, h => StartsWith(h, [0x1A, 0x45, 0xDF, 0xA3])),
    };

    // Shown in the file picker and error messages.
    public static IReadOnlyCollection<string> AllowedExtensions => Types.Keys;

    public static long MaxBytes(FileCategory category) => category switch
    {
        FileCategory.Video => MaxVideoBytes,
        FileCategory.Image => MaxImageBytes,
        _ => MaxDocumentBytes,
    };

    // The largest size any type may reach, used as the request-level cap.
    public static long LargestMaxBytes => MaxVideoBytes;

    public static FileTypeResult Validate(string? fileName, long size, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(extension) || !Types.TryGetValue(extension, out var type))
        {
            return FileTypeResult.Rejected($"File type '{extension}' is not supported. Allowed types: {string.Join(", ", Types.Keys.Order())}.");
        }

        if (size <= 0)
        {
            return FileTypeResult.Rejected("File cannot be empty.");
        }

        var max = MaxBytes(type.Category);
        if (size > max)
        {
            return FileTypeResult.Rejected($"File is too large. {type.Category} files can be at most {max / (1024 * 1024)} MB.");
        }

        if (!type.Matches(header.ToArray()))
        {
            return FileTypeResult.Rejected($"The file content does not match its '{extension}' extension.");
        }

        return FileTypeResult.Accepted(type.ContentType, type.Category);
    }

    public static FileCategory? CategoryOf(string? contentType) =>
        Types.Values.FirstOrDefault(t => string.Equals(t.ContentType, contentType, StringComparison.OrdinalIgnoreCase))?.Category;

    // Types the browser can show in place; everything else is offered as a download.
    public static bool IsInlinePreviewable(string? contentType) =>
        CategoryOf(contentType) is not null;

    private static bool StartsWith(byte[] header, ReadOnlySpan<byte> prefix) => At(header, 0, prefix);

    private static bool At(byte[] header, int offset, ReadOnlySpan<byte> expected) =>
        header.Length >= offset + expected.Length && header.AsSpan(offset, expected.Length).SequenceEqual(expected);
}

public sealed record FileTypeResult(bool IsValid, string? Error, string ContentType, FileCategory Category)
{
    public static FileTypeResult Accepted(string contentType, FileCategory category) => new(true, null, contentType, category);

    public static FileTypeResult Rejected(string error) => new(false, error, string.Empty, default);
}
