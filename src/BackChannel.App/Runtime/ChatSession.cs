using BackChannel.Core.Conversations;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using BackChannel.Core.Protocol;

namespace BackChannel.App.Runtime;

public sealed class ChatSession(BackChannelNode node)
{
    public Conversation? ActiveConversation { get; private set; }

    public Conversation Activate(Peer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var conversation = FindOneToOneConversation(peer)
                           ?? node.Conversations.Create(
                               peer.DisplayName,
                               [peer.Fingerprint]);

        ActiveConversation = conversation;
        return conversation;
    }

    public Conversation ActivateGroup(
        string name,
        IEnumerable<Peer> peers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(peers);

        var members = peers
            .Select(static peer => peer.Fingerprint)
            .Distinct()
            .ToArray();

        if (members.Length < 2)
        {
            throw new ArgumentException(
                "A group requires at least two remote peers.",
                nameof(peers));
        }

        var conversation = node.Conversations.Create(name.Trim(), members);
        ActiveConversation = conversation;
        return conversation;
    }

    public void Activate(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (!node.Conversations.TryGet(conversation.Id, out var registered)
            || !ReferenceEquals(registered, conversation))
        {
            throw new InvalidOperationException(
                "Only a registered conversation can be activated.");
        }

        ActiveConversation = conversation;
    }

    public Conversation Receive(ChatMessage message, Peer sender)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);

        var remoteFingerprints = message.ParticipantFingerprints
            .Select(static value => new Fingerprint(value))
            .Where(fingerprint => fingerprint != node.Fingerprint)
            .ToArray();

        if (!remoteFingerprints.Contains(sender.Fingerprint))
        {
            throw new InvalidDataException(
                "The sender is not a member of the signed conversation.");
        }

        foreach (var fingerprint in remoteFingerprints)
        {
            if (!node.Peers.TryGet(fingerprint, out _))
            {
                throw new InvalidDataException(
                    $"Conversation member {fingerprint.Value[..12]} has not been discovered.");
            }
        }

        if (node.Conversations.TryGet(message.ConversationId, out var existing)
            && existing is not null)
        {
            if (!string.Equals(
                    existing.Name,
                    message.ConversationName,
                    StringComparison.Ordinal)
                || !existing.Members.SequenceEqual(
                    remoteFingerprints.OrderBy(
                        static fingerprint => fingerprint.Value,
                        StringComparer.Ordinal)))
            {
                throw new InvalidDataException(
                    "Signed conversation metadata changed unexpectedly.");
            }

            ActiveConversation ??= existing;
            return existing;
        }

        var conversation = new Conversation(
            message.ConversationId,
            message.ConversationName,
            remoteFingerprints);
        if (!node.Conversations.TryAdd(conversation)
            && node.Conversations.TryGet(message.ConversationId, out existing)
            && existing is not null)
        {
            return existing;
        }

        ActiveConversation ??= conversation;
        return conversation;
    }

    public bool RemovePeer(Fingerprint fingerprint)
    {
        var activeWasRemoved = false;

        foreach (var conversation in node.Conversations.Snapshot())
        {
            if (!conversation.Contains(fingerprint)
                || !node.Conversations.Remove(conversation.Id, out _))
            {
                continue;
            }

            if (ActiveConversation?.Id == conversation.Id)
            {
                ActiveConversation = null;
                activeWasRemoved = true;
            }
        }

        return activeWasRemoved;
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
