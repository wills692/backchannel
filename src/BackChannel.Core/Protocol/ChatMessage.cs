namespace BackChannel.Core.Protocol;

public sealed record ChatMessage : Envelope
{
    public const int MaximumConversationNameLength = 128;

    public const int MaximumParticipantCount = 64;

    public required Guid ConversationId { get; init; }

    public required string ConversationName { get; init; }

    public required IReadOnlyList<string> ParticipantFingerprints { get; init; }

    public required string SenderFingerprint { get; init; }

    public required IReadOnlyList<RecipientKeyEnvelope> RecipientKeys { get; init; }

    public required string Nonce { get; init; }

    public required string Ciphertext { get; init; }

    public required string AuthenticationTag { get; init; }

    public required string Signature { get; init; }
}
