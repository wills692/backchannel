using System.Net;
using BackChannel.Core.Crypto;

namespace BackChannel.Core.Networking;

public sealed record LocalNode
{
    public LocalNode(
        string machineName,
        string displayName,
        PublicIdentity identity,
        int tcpPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(identity);

        if (tcpPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tcpPort),
                tcpPort,
                "An advertised TCP port must be between 1 and 65535.");
        }

        MachineName = machineName;
        DisplayName = displayName;
        Identity = identity;
        TcpPort = tcpPort;
    }

    public string MachineName { get; }

    public string DisplayName { get; }

    public PublicIdentity Identity { get; }

    public int TcpPort { get; }
}
