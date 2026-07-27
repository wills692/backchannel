using System.Net;

namespace BackChannel.Core.Networking;

public sealed record UdpDiscoveryOptions
{
    public const int DefaultPort = 52520;

    public IPAddress ListenAddress { get; init; } = IPAddress.Any;

    public int ListenPort { get; init; } = DefaultPort;

    public IPEndPoint? DefaultTarget { get; init; }

    public bool ReuseAddress { get; init; } = true;
}
