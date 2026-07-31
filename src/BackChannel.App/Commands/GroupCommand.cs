using BackChannel.App.Runtime;
using BackChannel.App.Ui;
using BackChannel.Core.Protocol;

namespace BackChannel.App.Commands;

public sealed class GroupCommand(
    BackChannelNode node,
    ChatSession session,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "group";

    public string Description => "Create or activate an encrypted group conversation.";

    public async Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            var existingGroups = node.Conversations.Snapshot()
                .Where(
                    conversation =>
                        conversation.Members.Count > 1
                        && string.Equals(
                            conversation.Name,
                            arguments.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (existingGroups.Length == 1)
            {
                session.Activate(existingGroups[0]);
                terminal.WriteInfo(
                    $"Active group: {existingGroups[0].Name} ({existingGroups[0].Members.Count + 1} participants).");
                return;
            }
        }

        var peers = node.Peers.Snapshot();
        if (peers.Count < 2)
        {
            terminal.WriteWarning(
                "A group requires at least two discovered peers.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(arguments)
            ? await terminal.ReadGroupNameAsync(cancellationToken)
                .ConfigureAwait(false)
            : arguments.Trim();

        if (string.IsNullOrWhiteSpace(name)
            || name.Length > ChatMessage.MaximumConversationNameLength)
        {
            terminal.WriteWarning(
                $"Group names must contain 1-{ChatMessage.MaximumConversationNameLength} characters.");
            return;
        }

        var selectedPeers = await terminal.SelectPeersAsync(
                peers,
                cancellationToken)
            .ConfigureAwait(false);
        if (selectedPeers.Count < 2)
        {
            terminal.WriteWarning("Select at least two peers.");
            return;
        }

        var conversation = session.ActivateGroup(name, selectedPeers);
        terminal.WriteInfo(
            $"Active group: {conversation.Name} ({conversation.Members.Count + 1} participants).");
    }
}
