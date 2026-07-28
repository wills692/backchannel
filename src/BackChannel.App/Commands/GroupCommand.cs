using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class GroupCommand(
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "group";

    public string Description => "Create a group conversation (milestone 5).";

    public Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        terminal.WriteInfo(
            "Group conversation creation arrives in milestone 5.");
        return Task.CompletedTask;
    }
}
