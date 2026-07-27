using System.Net;
using BackChannel.Core.Crypto;

namespace BackChannel.Core.Peers;

public sealed record Peer
{
    private readonly IPEndPoint _tcpEndpoint;

    public Peer(
        string machineName,
        string displayName,
        PublicIdentity identity,
        IPEndPoint tcpEndpoint,
        DateTimeOffset lastSeenUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(tcpEndpoint);

        MachineName = machineName;
        DisplayName = displayName;
        Identity = identity;
        _tcpEndpoint = new IPEndPoint(tcpEndpoint.Address, tcpEndpoint.Port);
        LastSeenUtc = lastSeenUtc;
    }

    public Fingerprint Fingerprint => Identity.Fingerprint;

    public string MachineName { get; }

    public string DisplayName { get; }

    public PublicIdentity Identity { get; }

    public IPEndPoint TcpEndpoint =>
        new(_tcpEndpoint.Address, _tcpEndpoint.Port);

    public DateTimeOffset LastSeenUtc { get; }
}
