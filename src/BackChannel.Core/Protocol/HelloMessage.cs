namespace BackChannel.Core.Protocol;

public sealed record HelloMessage : Envelope
{
    public required string MachineName { get; init; }

    public required string DisplayName { get; init; }

    public required string PublicKey { get; init; }

    public required int TcpPort { get; init; }
}
