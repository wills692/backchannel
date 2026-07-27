using System.Buffers.Binary;
using System.Text.Json;

namespace BackChannel.Core.Protocol;

public static class TcpFrameCodec
{
    public const int HeaderLength = sizeof(int);
    public const int DefaultMaximumFrameLength = 1024 * 1024;

    public static async ValueTask WriteAsync(
        Stream stream,
        Envelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(envelope);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BackChannelJsonContext.Default.Envelope);

        if (payload.Length > DefaultMaximumFrameLength)
        {
            throw new InvalidDataException(
                $"Serialized frame length {payload.Length} exceeds the maximum of {DefaultMaximumFrameLength} bytes.");
        }

        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<Envelope?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderLength];
        if (!await ReadExactlyAsync(stream, header, allowCleanEndOfStream: true, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (payloadLength <= 0 || payloadLength > DefaultMaximumFrameLength)
        {
            throw new InvalidDataException(
                $"Frame length {payloadLength} must be between 1 and {DefaultMaximumFrameLength} bytes.");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, allowCleanEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize(
                   payload,
                   BackChannelJsonContext.Default.Envelope)
               ?? throw new InvalidDataException("The frame contained a null JSON envelope.");
    }

    private static async ValueTask<bool> ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        bool allowCleanEndOfStream,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer[totalRead..], cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                if (allowCleanEndOfStream && totalRead == 0)
                {
                    return false;
                }

                throw new EndOfStreamException(
                    $"The stream ended after {totalRead} of {buffer.Length} expected bytes.");
            }

            totalRead += bytesRead;
        }

        return true;
    }
}
