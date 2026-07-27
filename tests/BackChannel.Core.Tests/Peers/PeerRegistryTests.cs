using System.Net;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using Xunit;

namespace BackChannel.Core.Tests.Peers;

public sealed class PeerRegistryTests
{
    [Fact]
    public void ReannounceWithSameFingerprintRefreshesExistingPeer()
    {
        using var identity = IdentityKeyPair.Create(2048);
        var registry = new PeerRegistry();
        var firstSeen = new DateTimeOffset(
            2026,
            7,
            27,
            12,
            0,
            0,
            TimeSpan.Zero);
        var secondSeen = firstSeen.AddMinutes(1);
        var first = CreatePeer(identity, "ALICE-PC", "Alice", 41001, firstSeen);
        var refreshed = CreatePeer(identity, "ALICE-PC", "Alice", 41001, secondSeen);

        Assert.Equal(PeerRegistrationChange.Added, registry.Upsert(first));
        Assert.Equal(PeerRegistrationChange.Refreshed, registry.Upsert(refreshed));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(identity.Fingerprint, out var current));
        Assert.NotNull(current);
        Assert.Equal(secondSeen, current.LastSeenUtc);
    }

    [Fact]
    public void ChangedContactDetailsUpdatePeerWithSameFingerprint()
    {
        using var identity = IdentityKeyPair.Create(2048);
        var registry = new PeerRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.Upsert(CreatePeer(identity, "ALICE-PC", "Alice", 41001, now));
        var change = registry.Upsert(
            CreatePeer(identity, "ALICE-LAPTOP", "Alice", 41099, now.AddMinutes(1)));

        Assert.Equal(PeerRegistrationChange.Updated, change);
        Assert.True(registry.TryGet(identity.Fingerprint, out var current));
        Assert.NotNull(current);
        Assert.Equal("ALICE-LAPTOP", current.MachineName);
        Assert.Equal(41099, current.TcpEndpoint.Port);
    }

    [Fact]
    public void SameDisplayNameWithDifferentKeysCreatesDistinctPeers()
    {
        using var firstIdentity = IdentityKeyPair.Create(2048);
        using var secondIdentity = IdentityKeyPair.Create(2048);
        var registry = new PeerRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.Upsert(CreatePeer(firstIdentity, "FIRST-PC", "Alex", 41001, now));
        registry.Upsert(CreatePeer(secondIdentity, "SECOND-PC", "Alex", 41002, now));

        Assert.Equal(2, registry.Count);
        Assert.True(registry.TryGet(firstIdentity.Fingerprint, out _));
        Assert.True(registry.TryGet(secondIdentity.Fingerprint, out _));
    }

    [Fact]
    public void RemoveStaleOnlyRemovesPeersBeforeCutoff()
    {
        using var staleIdentity = IdentityKeyPair.Create(2048);
        using var currentIdentity = IdentityKeyPair.Create(2048);
        var registry = new PeerRegistry();
        var cutoff = new DateTimeOffset(
            2026,
            7,
            27,
            12,
            0,
            0,
            TimeSpan.Zero);

        registry.Upsert(
            CreatePeer(staleIdentity, "OLD-PC", "Old", 41001, cutoff.AddSeconds(-1)));
        registry.Upsert(
            CreatePeer(currentIdentity, "NEW-PC", "New", 41002, cutoff));

        var removed = registry.RemoveStale(cutoff);

        var stalePeer = Assert.Single(removed);
        Assert.Equal(staleIdentity.Fingerprint, stalePeer.Fingerprint);
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(currentIdentity.Fingerprint, out _));
    }

    private static Peer CreatePeer(
        IdentityKeyPair identity,
        string machineName,
        string displayName,
        int port,
        DateTimeOffset lastSeenUtc) =>
        new(
            machineName,
            displayName,
            identity.PublicIdentity,
            new IPEndPoint(IPAddress.Loopback, port),
            lastSeenUtc);
}
