using BackChannel.Core.Conversations;
using BackChannel.Core.Peers;

namespace BackChannel.App.Runtime;

public sealed class ChatSession(BackChannelNode node)
{
    public Conversation? ActiveConversation { get; private set; }

    public Peer? ActivePeer { get; private set; }

    public Conversation Activate(Peer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var conversation = FindOneToOneConversation(peer)
                           ?? node.Conversations.Create(
                               peer.DisplayName,
                               [peer.Fingerprint]);

        ActiveConversation = conversation;
        ActivePeer = peer;
        return conversation;
    }

    public Conversation Receive(Peer peer, Guid conversationId)
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (!node.Conversations.TryGet(conversationId, out var conversation)
            || conversation is null)
        {
            conversation = new Conversation(
                conversationId,
                peer.DisplayName,
                [peer.Fingerprint]);
            node.Conversations.TryAdd(conversation);
        }

        return conversation;
    }

    public Task SendAsync(
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        if (ActiveConversation is null)
        {
            throw new InvalidOperationException(
                "No conversation is active. Use /msg to select a peer.");
        }

        return node.SendMessageAsync(
            ActiveConversation,
            plaintext,
            cancellationToken);
    }

    private Conversation? FindOneToOneConversation(Peer peer) =>
        node.Conversations.Snapshot().FirstOrDefault(
            conversation =>
                conversation.Members.Count == 1
                && conversation.Contains(peer.Fingerprint));
}
