using BackChannel.App.Runtime;
using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class NickCommand(
    BackChannelNode node,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "nick";

    public string Description => "Change your display name and announce it to peers.";

    public async Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var displayName = arguments.Trim();
        if (!BackChannelNode.IsValidDisplayName(displayName))
        {
            terminal.WriteWarning(
                $"Display names must contain 1-{BackChannelNode.MaximumDisplayNameLength} characters.");
            return;
        }

        var previousDisplayName = node.DisplayName;
        if (string.Equals(
                previousDisplayName,
                displayName,
                StringComparison.Ordinal))
        {
            terminal.WriteInfo($"Display name is already {displayName}.");
            return;
        }

        await node.ChangeDisplayNameAsync(displayName, cancellationToken)
            .ConfigureAwait(false);

        terminal.WriteInfo(
            $"Display name changed from {previousDisplayName} to {displayName}. Presence broadcast sent.");
    }
}
