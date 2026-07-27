using System.Buffers.Binary;
using BackChannel.Core.Protocol;
using Xunit;

namespace BackChannel.Core.Tests.Protocol;

public sealed class TcpFrameCodecTests
{
    [Fact]
    public async Task FrameRoundTripsAcrossPartialReads()
    {
        var expected = new HelloMessage
        {
            MachineName = "ALICE-PC",
            DisplayName = "Alice",
            PublicKey = "public-key",
            TcpPort = 52521,
        };

        await using var encoded = new MemoryStream();
        await TcpFrameCodec.WriteAsync(
            encoded,
            expected,
            TestContext.Current.CancellationToken);
        await using var chunked = new ChunkedReadStream(encoded.ToArray(), maximumChunkSize: 2);

        var actual = Assert.IsType<HelloMessage>(
            await TcpFrameCodec.ReadAsync(
                chunked,
                TestContext.Current.CancellationToken));

        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.MachineName, actual.MachineName);
        Assert.Equal(expected.TcpPort, actual.TcpPort);
    }

    [Fact]
    public async Task CleanEndOfStreamReturnsNull()
    {
        await using var stream = new MemoryStream();

        Assert.Null(
            await TcpFrameCodec.ReadAsync(
                stream,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartialHeaderThrows()
    {
        await using var stream = new MemoryStream([0, 0]);

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await TcpFrameCodec.ReadAsync(
                stream,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartialPayloadThrows()
    {
        var bytes = new byte[TcpFrameCodec.HeaderLength + 2];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 10);
        await using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await TcpFrameCodec.ReadAsync(
                stream,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(TcpFrameCodec.DefaultMaximumFrameLength + 1)]
    public async Task InvalidFrameLengthIsRejected(int frameLength)
    {
        var header = new byte[TcpFrameCodec.HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, frameLength);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TcpFrameCodec.ReadAsync(
                stream,
                TestContext.Current.CancellationToken));
    }

    private sealed class ChunkedReadStream(byte[] bytes, int maximumChunkSize)
        : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var limitedBuffer = buffer[..Math.Min(buffer.Length, maximumChunkSize)];
            return base.ReadAsync(limitedBuffer, cancellationToken);
        }
    }
}
