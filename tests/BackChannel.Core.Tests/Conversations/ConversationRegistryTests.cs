using BackChannel.Core.Conversations;
using BackChannel.Core.Crypto;
using Xunit;

namespace BackChannel.Core.Tests.Conversations;

public sealed class ConversationRegistryTests
{
    [Fact]
    public void CreateNormalizesAndDeduplicatesMembers()
    {
        using var alice = IdentityKeyPair.Create(2048);
        using var bob = IdentityKeyPair.Create(2048);
        var registry = new ConversationRegistry();

        var conversation = registry.Create(
            "Router Crew",
            [bob.Fingerprint, alice.Fingerprint, bob.Fingerprint]);

        Assert.NotEqual(Guid.Empty, conversation.Id);
        Assert.Equal(2, conversation.Members.Count);
        Assert.True(conversation.Contains(alice.Fingerprint));
        Assert.True(conversation.Contains(bob.Fingerprint));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(conversation.Id, out var registered));
        Assert.Same(conversation, registered);
    }

    [Fact]
    public void EmptyMemberSetIsRejected()
    {
        var registry = new ConversationRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.Create("Nobody", []));
    }

    [Fact]
    public void DuplicateConversationIdIsRejectedWithoutReplacement()
    {
        using var alice = IdentityKeyPair.Create(2048);
        var registry = new ConversationRegistry();
        var id = Guid.NewGuid();
        var first = new Conversation(id, "First", [alice.Fingerprint]);
        var duplicate = new Conversation(id, "Duplicate", [alice.Fingerprint]);

        Assert.True(registry.TryAdd(first));
        Assert.False(registry.TryAdd(duplicate));
        Assert.True(registry.TryGet(id, out var registered));
        Assert.Same(first, registered);
    }
}
