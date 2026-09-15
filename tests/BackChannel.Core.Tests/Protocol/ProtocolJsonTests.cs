using System.Text;
using System.Text.Json;
using BackChannel.Core.Protocol;
using Xunit;

namespace BackChannel.Core.Tests.Protocol;

public sealed class ProtocolJsonTests
{
    [Fact]
    public void EveryEnvelopeTypeRoundTripsPolymorphically()
    {
        Assert.Equal(3, Envelope.CurrentProtocolVersion);

        Envelope[] messages =
        [
            new HelloMessage
            {
                MachineName = "ALICE-PC",
                DisplayName = "Alice",
                PublicKey = "hello-key",
                TcpPort = 41001,
            },
            new AnnounceMessage
            {
                MachineName = "BOB-PC",
                DisplayName = "Bob",
                PublicKey = "announce-key",
                TcpPort = 41002,
            },
            new ChatMessage
            {
                ConversationId = Guid.NewGuid(),
                ConversationName = "Router Crew",
                ParticipantFingerprints =
                [
                    new string('a', 64),
                    new string('b', 64),
                ],
                SenderFingerprint = new string('a', 64),
                RecipientKeys =
                [
                    new RecipientKeyEnvelope
                    {
                        RecipientFingerprint = new string('b', 64),
                        WrappedKey = "wrapped-key",
                    },
                ],
                Nonce = "nonce",
                Ciphertext = "ciphertext",
                AuthenticationTag = "tag",
                Signature = "signature",
            },
            new GoodbyeMessage
            {
                SenderFingerprint = new string('c', 64),
            },
        ];

        foreach (var message in messages)
        {
            var json = JsonSerializer.Serialize(
                message,
                BackChannelJsonContext.Default.Envelope);
            var roundTripped = JsonSerializer.Deserialize(
                json,
                BackChannelJsonContext.Default.Envelope);

            Assert.NotNull(roundTripped);
            Assert.IsType(message.GetType(), roundTripped);
            Assert.Equal(message.MessageId, roundTripped.MessageId);
            Assert.Equal(message.ProtocolVersion, roundTripped.ProtocolVersion);
            Assert.Contains("\"type\":", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownEnvelopeTypeIsRejected()
    {
        const string json =
            """{"type":"unknown","protocolVersion":1,"messageId":"00000000-0000-0000-0000-000000000000","sentAtUtc":"2026-01-01T00:00:00Z"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(
                Encoding.UTF8.GetBytes(json),
                BackChannelJsonContext.Default.Envelope));
    }

    [Fact]
    public void FileTransferPayloadsRoundTrip()
    {
        var offer = new FileOfferPayload
        {
            TransferId = Guid.NewGuid(),
            FileName = "report.pdf",
            Length = 1024,
            Sha256 = new string('a', 64),
        };
        var chunk = new FileChunkPayload
        {
            TransferId = offer.TransferId,
            Sequence = 2,
            IsFinal = true,
            Data = "Zm9v",
        };

        var roundTrippedOffer = JsonSerializer.Deserialize<FileOfferPayload>(
            JsonSerializer.Serialize(offer));
        var roundTrippedChunk = JsonSerializer.Deserialize<FileChunkPayload>(
            JsonSerializer.Serialize(chunk));

        Assert.Equal(offer, roundTrippedOffer);
        Assert.Equal(chunk, roundTrippedChunk);
    }
}
