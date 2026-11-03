using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class FileTypePolicyTests
{
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
    private static readonly byte[] Mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2'];
    private static readonly byte[] WebP = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];

    public static TheoryData<string, byte[], string, FileCategory> ValidFiles => new()
    {
        { "claim.pdf", Pdf, "application/pdf", FileCategory.Document },
        { "NOTES.TXT", "hello"u8.ToArray(), "text/plain", FileCategory.Document },
        { "damage.JPG", Jpeg, "image/jpeg", FileCategory.Image },
        { "damage.jpeg", Jpeg, "image/jpeg", FileCategory.Image },
        { "damage.png", Png, "image/png", FileCategory.Image },
        { "damage.webp", WebP, "image/webp", FileCategory.Image },
        { "walkthrough.mp4", Mp4, "video/mp4", FileCategory.Video },
    };

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void Validate_MatchingExtensionAndContent_ReturnsDetectedType(string name, byte[] header, string contentType, FileCategory category)
    {
        var result = FileTypePolicy.Validate(name, header.Length, header);

        Assert.True(result.IsValid);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(category, result.Category);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("run.js")]
    [InlineData("macro.docm")]
    [InlineData("logo.svg")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_UnsupportedExtension_IsRejected(string? name)
    {
        var result = FileTypePolicy.Validate(name, 10, Pdf);

        Assert.False(result.IsValid);
        Assert.Contains("not supported", result.Error);
    }

    [Fact]
    public void Validate_ExecutableRenamedToPdf_IsRejected()
    {
        var result = FileTypePolicy.Validate("invoice.pdf", 100, "MZ\x90\0"u8);

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Error);
    }

    [Fact]
    public void Validate_PngNamedAsJpg_IsRejected()
    {
        Assert.False(FileTypePolicy.Validate("photo.jpg", Png.Length, Png).IsValid);
    }

    [Fact]
    public void Validate_TextFileWithBinaryContent_IsRejected()
    {
        Assert.False(FileTypePolicy.Validate("notes.txt", 4, [0x41, 0x00, 0x42, 0x43]).IsValid);
    }

    [Fact]
    public void Validate_EmptyFile_IsRejected()
    {
        Assert.False(FileTypePolicy.Validate("a.pdf", 0, ReadOnlySpan<byte>.Empty).IsValid);
    }

    [Theory]
    [InlineData("a.pdf", FileTypePolicy.MaxDocumentBytes)]
    [InlineData("a.png", FileTypePolicy.MaxImageBytes)]
    [InlineData("a.mp4", FileTypePolicy.MaxVideoBytes)]
    public void Validate_SizeAtLimit_IsAcceptedAndOverLimitRejected(string name, long max)
    {
        var header = name.EndsWith(".pdf") ? Pdf : name.EndsWith(".png") ? Png : Mp4;

        Assert.True(FileTypePolicy.Validate(name, max, header).IsValid);
        Assert.False(FileTypePolicy.Validate(name, max + 1, header).IsValid);
    }

    [Fact]
    public void CategoryOf_UnknownContentType_IsNull()
    {
        Assert.Null(FileTypePolicy.CategoryOf("application/octet-stream"));
        Assert.Equal(FileCategory.Video, FileTypePolicy.CategoryOf("VIDEO/MP4"));
    }
}
