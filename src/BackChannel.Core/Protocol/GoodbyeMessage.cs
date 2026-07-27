namespace BackChannel.Core.Protocol;

public sealed record GoodbyeMessage : Envelope
{
    public required string SenderFingerprint { get; init; }
}
