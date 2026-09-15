namespace BackChannel.Core.Protocol;

public sealed record ChatMessage : Envelope
{
    public static class ContentTypes
    {
        public const string Chat = "chat";
        public const string FileOffer = "file-offer";
        public const string FileResponse = "file-response";
        public const string FileChunk = "file-chunk";
    }

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

    public string ContentType { get; init; } = ContentTypes.Chat;

    public required string Signature { get; init; }
}
