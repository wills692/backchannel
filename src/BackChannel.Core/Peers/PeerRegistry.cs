using System.Collections.Concurrent;
using BackChannel.Core.Crypto;

namespace BackChannel.Core.Peers;

public sealed class PeerRegistry
{
    private readonly ConcurrentDictionary<Fingerprint, Peer> _peers = new();

    public int Count => _peers.Count;

    public PeerRegistrationChange Upsert(Peer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        while (true)
        {
            if (!_peers.TryGetValue(peer.Fingerprint, out var existing))
            {
                if (_peers.TryAdd(peer.Fingerprint, peer))
                {
                    return PeerRegistrationChange.Added;
                }

                continue;
            }

            var change = HasSameContactDetails(existing, peer)
                ? PeerRegistrationChange.Refreshed
                : PeerRegistrationChange.Updated;

            if (_peers.TryUpdate(peer.Fingerprint, peer, existing))
            {
                return change;
            }
        }
    }

    public bool TryGet(Fingerprint fingerprint, out Peer? peer) =>
        _peers.TryGetValue(fingerprint, out peer);

    public bool Remove(Fingerprint fingerprint, out Peer? peer) =>
        _peers.TryRemove(fingerprint, out peer);

    public IReadOnlyList<Peer> Snapshot() =>
        _peers.Values
            .OrderBy(static peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static peer => peer.Fingerprint.Value, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<Peer> RemoveStale(DateTimeOffset lastSeenBefore)
    {
        var removed = new List<Peer>();

        foreach (var entry in _peers)
        {
            if (entry.Value.LastSeenUtc >= lastSeenBefore)
            {
                continue;
            }

            if (_peers.TryRemove(entry))
            {
                removed.Add(entry.Value);
            }
        }

        return removed;
    }

    private static bool HasSameContactDetails(Peer left, Peer right) =>
        string.Equals(left.MachineName, right.MachineName, StringComparison.Ordinal)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && left.TcpEndpoint.Equals(right.TcpEndpoint);
}
