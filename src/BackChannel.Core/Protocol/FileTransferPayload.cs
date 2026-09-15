namespace BackChannel.Core.Protocol;

public sealed record FileOfferPayload
{
    public required Guid TransferId { get; init; }

    public required string FileName { get; init; }

    public required long Length { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record FileResponsePayload
{
    public required Guid TransferId { get; init; }

    public required bool Accepted { get; init; }
}

public sealed record FileChunkPayload
{
    public required Guid TransferId { get; init; }

    public required int Sequence { get; init; }

    public required bool IsFinal { get; init; }

    public required string Data { get; init; }
}