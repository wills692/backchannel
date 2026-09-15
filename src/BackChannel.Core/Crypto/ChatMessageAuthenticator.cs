using System.Buffers.Binary;
using System.Text;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Crypto;

internal static class ChatMessageAuthenticator
{
    private static readonly byte[] DomainSeparator =
        "BackChannel.ChatMessage.Signature.v2"u8.ToArray();

    internal static byte[] CreateSignaturePayload(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var stream = new MemoryStream();
        WriteBytes(stream, DomainSeparator);
        WriteInt32(stream, message.ProtocolVersion);
        WriteBytes(stream, message.MessageId.ToByteArray());
        WriteInt64(stream, message.SentAtUtc.UtcTicks);
        WriteBytes(stream, message.ConversationId.ToByteArray());
        WriteString(stream, message.ConversationName);
        WriteInt32(stream, message.ParticipantFingerprints.Count);

        foreach (var participant in message.ParticipantFingerprints)
        {
            WriteString(stream, participant);
        }

        WriteString(stream, message.SenderFingerprint);
        WriteString(stream, message.ContentType);
        WriteInt32(stream, message.RecipientKeys.Count);

        foreach (var recipient in message.RecipientKeys)
        {
            WriteString(stream, recipient.RecipientFingerprint);
            WriteString(stream, recipient.WrappedKey);
        }

        WriteString(stream, message.Nonce);
        WriteString(stream, message.Ciphertext);
        WriteString(stream, message.AuthenticationTag);
        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteBytes(stream, Encoding.UTF8.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
