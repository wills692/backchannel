using System.Text.Json.Serialization;

namespace BackChannel.Core.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(AnnounceMessage), "announce")]
[JsonDerivedType(typeof(ChatMessage), "chat")]
[JsonDerivedType(typeof(GoodbyeMessage), "goodbye")]
public abstract record Envelope
{
    public const int CurrentProtocolVersion = 3;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;

    public Guid MessageId { get; init; } = Guid.NewGuid();

    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
