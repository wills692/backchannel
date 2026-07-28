using BackChannel.App.Runtime;
using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class PeersCommand(
    BackChannelNode node,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "peers";

    public string Description => "Show currently discovered peers.";

    public Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        terminal.ShowPeers(node.Peers.Snapshot());
        return Task.CompletedTask;
    }
}
