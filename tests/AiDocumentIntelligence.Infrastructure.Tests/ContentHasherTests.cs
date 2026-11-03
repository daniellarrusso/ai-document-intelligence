using System.Text;
using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public class ContentHasherTests
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task ComputeAsync_KnownInput_ReturnsLowercaseHexSha256()
    {
        // The standard SHA-256 test vector for "abc".
        var hash = await ContentHasher.ComputeAsync(Bytes("abc"));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.Equal(ContentHasher.HexLength, hash.Length);
    }

    [Fact]
    public async Task ComputeAsync_EmptyContent_ReturnsTheEmptyInputHash()
    {
        var hash = await ContentHasher.ComputeAsync(new MemoryStream());

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public async Task ComputeAsync_SameBytes_SameHash_DifferentBytes_DifferentHash()
    {
        var first = await ContentHasher.ComputeAsync(Bytes("police report"));
        var again = await ContentHasher.ComputeAsync(Bytes("police report"));
        var other = await ContentHasher.ComputeAsync(Bytes("police report."));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task ComputeAsync_StreamNotAtStart_HashesTheWholeContentAndRestoresThePosition()
    {
        var stream = Bytes("abc");
        stream.Position = 2;

        var hash = await ContentHasher.ComputeAsync(stream);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public async Task ComputeAsync_NonSeekableStream_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ContentHasher.ComputeAsync(new NonSeekableStream()));
    }

    [Fact]
    public async Task ComputeAsync_Cancelled_ThrowsAndStillRestoresThePosition()
    {
        var stream = Bytes("abc");
        stream.Position = 1;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContentHasher.ComputeAsync(stream, cts.Token));

        Assert.Equal(1, stream.Position);
    }

    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
