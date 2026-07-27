using System.Collections.Concurrent;
using BackChannel.Core.Crypto;

namespace BackChannel.Core.Conversations;

public sealed class ConversationRegistry
{
    private readonly ConcurrentDictionary<Guid, Conversation> _conversations = new();

    public int Count => _conversations.Count;

    public Conversation Create(string name, IEnumerable<Fingerprint> members)
    {
        var conversation = new Conversation(Guid.NewGuid(), name, members);

        if (!_conversations.TryAdd(conversation.Id, conversation))
        {
            throw new InvalidOperationException(
                $"Conversation ID {conversation.Id} is already registered.");
        }

        return conversation;
    }

    public bool TryAdd(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return _conversations.TryAdd(conversation.Id, conversation);
    }

    public bool TryGet(Guid id, out Conversation? conversation) =>
        _conversations.TryGetValue(id, out conversation);

    public bool Remove(Guid id, out Conversation? conversation) =>
        _conversations.TryRemove(id, out conversation);

    public IReadOnlyList<Conversation> Snapshot() =>
        _conversations.Values
            .OrderBy(static conversation => conversation.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static conversation => conversation.Id)
            .ToArray();
}
