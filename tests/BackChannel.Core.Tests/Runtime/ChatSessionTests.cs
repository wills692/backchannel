using System.Net;
using BackChannel.App.Configuration;
using BackChannel.App.Runtime;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BackChannel.Core.Tests.Runtime;

public sealed class ChatSessionTests
{
    [Fact]
    public async Task ActivateGroupCreatesAnActiveMultiPeerConversation()
    {
        await using var node = CreateNode();
        using var aliceIdentity = IdentityKeyPair.Create(2048);
        using var bobIdentity = IdentityKeyPair.Create(2048);
        var alice = CreatePeer("Alice", aliceIdentity, 41001);
        var bob = CreatePeer("Bob", bobIdentity, 41002);
        node.Peers.Upsert(alice);
        node.Peers.Upsert(bob);
        var session = new ChatSession(node);

        var conversation = session.ActivateGroup(
            "Router Crew",
            [alice, bob, alice]);

        Assert.Same(conversation, session.ActiveConversation);
        Assert.Equal("Router Crew", conversation.Name);
        Assert.Equal(2, conversation.Members.Count);
        Assert.True(conversation.Contains(alice.Fingerprint));
        Assert.True(conversation.Contains(bob.Fingerprint));
    }

    [Fact]
    public async Task ReceiveReconstructsSignedGroupForLocalReply()
    {
        await using var node = CreateNode();
        using var aliceIdentity = IdentityKeyPair.Create(2048);
        using var bobIdentity = IdentityKeyPair.Create(2048);
        var alice = CreatePeer("Alice", aliceIdentity, 41001);
        var bob = CreatePeer("Bob", bobIdentity, 41002);
        node.Peers.Upsert(alice);
        node.Peers.Upsert(bob);
        var session = new ChatSession(node);
        var message = HybridCipher.Encrypt(
            "hello group",
            Guid.NewGuid(),
            "Router Crew",
            aliceIdentity,
            [node.PublicIdentity, bobIdentity.PublicIdentity]);

        Assert.Equal("hello group", node.DecryptMessage(message, alice));
        var conversation = session.Receive(message, alice);

        Assert.Equal(message.ConversationId, conversation.Id);
        Assert.Equal("Router Crew", conversation.Name);
        Assert.Equal(2, conversation.Members.Count);
        Assert.True(conversation.Contains(alice.Fingerprint));
        Assert.True(conversation.Contains(bob.Fingerprint));
        Assert.Same(conversation, session.ActiveConversation);
    }

    [Fact]
    public async Task RemovingPeerClosesAffectedActiveConversation()
    {
        await using var node = CreateNode();
        using var aliceIdentity = IdentityKeyPair.Create(2048);
        using var bobIdentity = IdentityKeyPair.Create(2048);
        var alice = CreatePeer("Alice", aliceIdentity, 41001);
        var bob = CreatePeer("Bob", bobIdentity, 41002);
        node.Peers.Upsert(alice);
        node.Peers.Upsert(bob);
        var session = new ChatSession(node);
        var conversation = session.ActivateGroup("Router Crew", [alice, bob]);

        var activeClosed = session.RemovePeer(alice.Fingerprint);

        Assert.True(activeClosed);
        Assert.Null(session.ActiveConversation);
        Assert.False(node.Conversations.TryGet(conversation.Id, out _));
    }

    [Fact]
    public async Task ReceiveRejectsGroupWithUndiscoveredMember()
    {
        await using var node = CreateNode();
        using var aliceIdentity = IdentityKeyPair.Create(2048);
        using var unknownIdentity = IdentityKeyPair.Create(2048);
        var alice = CreatePeer("Alice", aliceIdentity, 41001);
        node.Peers.Upsert(alice);
        var session = new ChatSession(node);
        var message = HybridCipher.Encrypt(
            "incomplete group",
            Guid.NewGuid(),
            "Router Crew",
            aliceIdentity,
            [node.PublicIdentity, unknownIdentity.PublicIdentity]);

        Assert.Equal(
            "incomplete group",
            node.DecryptMessage(message, alice));
        Assert.Throws<InvalidDataException>(() =>
            session.Receive(message, alice));
        Assert.Empty(node.Conversations.Snapshot());
    }

    private static BackChannelNode CreateNode() =>
        new(
            Options.Create(new BackChannelOptions()),
            NullLogger<BackChannelNode>.Instance);

    private static Peer CreatePeer(
        string name,
        IdentityKeyPair identity,
        int port) =>
        new(
            $"{name.ToUpperInvariant()}-PC",
            name,
            identity.PublicIdentity,
            new IPEndPoint(IPAddress.Loopback, port),
            DateTimeOffset.UtcNow);
}
