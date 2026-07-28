using Microsoft.Extensions.Hosting;

namespace BackChannel.App.Commands;

public sealed class QuitCommand(
    IHostApplicationLifetime applicationLifetime) : IBackChannelCommand
{
    public string Name => "quit";

    public string Description => "Send a goodbye notice and exit.";

    public Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        applicationLifetime.StopApplication();
        return Task.CompletedTask;
    }
}
