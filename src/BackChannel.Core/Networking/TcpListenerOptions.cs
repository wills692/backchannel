using System.Net;

namespace BackChannel.Core.Networking;

public sealed record TcpListenerOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Any;

    public int ListenPort { get; init; }

    public int MaximumConcurrentConnections { get; init; } = 32;
}
