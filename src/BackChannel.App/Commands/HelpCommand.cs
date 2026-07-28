using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class HelpCommand(
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    private static readonly (string Name, string Description)[] Commands =
    [
        ("group", "Create a group conversation (milestone 5)."),
        ("hello", "Broadcast your presence on the LAN."),
        ("help", "Show available commands."),
        ("msg", "Select a peer, optionally using /msg <name> [message]."),
        ("peers", "Show currently discovered peers."),
        ("quit", "Send a goodbye notice and exit."),
    ];

    public string Name => "help";

    public string Description => "Show available commands.";

    public Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        terminal.ShowCommands(Commands);
        return Task.CompletedTask;
    }
}
