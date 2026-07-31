using BackChannel.Core.Crypto;

namespace BackChannel.Core.Conversations;

public sealed class Conversation
{
    private readonly Fingerprint[] _members;
    private readonly IReadOnlyList<Fingerprint> _membersView;

    public Conversation(Guid id, string name, IEnumerable<Fingerprint> members)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A conversation ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);

        if (name.Length > Protocol.ChatMessage.MaximumConversationNameLength)
        {
            throw new ArgumentException(
                $"Conversation names cannot exceed {Protocol.ChatMessage.MaximumConversationNameLength} characters.",
                nameof(name));
        }

        _members = members
            .Distinct()
            .OrderBy(static fingerprint => fingerprint.Value, StringComparer.Ordinal)
            .ToArray();

        if (_members.Length == 0)
        {
            throw new ArgumentException(
                "A conversation must contain at least one member.",
                nameof(members));
        }

        if (_members.Length >= Protocol.ChatMessage.MaximumParticipantCount)
        {
            throw new ArgumentException(
                $"A conversation cannot exceed {Protocol.ChatMessage.MaximumParticipantCount} total participants.",
                nameof(members));
        }

        _membersView = Array.AsReadOnly(_members);
        Id = id;
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<Fingerprint> Members => _membersView;

    public bool Contains(Fingerprint fingerprint) =>
        Array.BinarySearch(
            _members,
            fingerprint,
            FingerprintComparer.Instance) >= 0;

    private sealed class FingerprintComparer : IComparer<Fingerprint>
    {
        internal static FingerprintComparer Instance { get; } = new();

        public int Compare(Fingerprint x, Fingerprint y) =>
            StringComparer.Ordinal.Compare(x.Value, y.Value);
    }
}
