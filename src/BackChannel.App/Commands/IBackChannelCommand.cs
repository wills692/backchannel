namespace BackChannel.App.Commands;

public interface IBackChannelCommand
{
    string Name { get; }

    string Description { get; }

    Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken);
}
