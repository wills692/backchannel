namespace BackChannel.Core.Protocol;

public sealed record RecipientKeyEnvelope
{
    public required string RecipientFingerprint { get; init; }

    public required string WrappedKey { get; init; }
}
