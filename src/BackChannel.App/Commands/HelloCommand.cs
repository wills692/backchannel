using BackChannel.App.Runtime;
using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class HelloCommand(
    BackChannelNode node,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "hello";

    public string Description => "Broadcast your presence on the LAN.";

    public async Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        await node.BroadcastHelloAsync(cancellationToken).ConfigureAwait(false);
        terminal.WriteInfo("Presence broadcast sent.");
    }
}
