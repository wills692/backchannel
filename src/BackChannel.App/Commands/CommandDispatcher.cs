using BackChannel.App.Runtime;
using BackChannel.App.Ui;

namespace BackChannel.App.Commands;

public sealed class CommandDispatcher
{
    private readonly Dictionary<string, IBackChannelCommand> _commands;
    private readonly ChatSession _session;
    private readonly IBackChannelTerminal _terminal;

    public CommandDispatcher(
        IEnumerable<IBackChannelCommand> commands,
        ChatSession session,
        IBackChannelTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(terminal);

        _commands = commands.ToDictionary(
            static command => command.Name,
            StringComparer.OrdinalIgnoreCase);
        _session = session;
        _terminal = terminal;
    }

    public async Task DispatchAsync(
        string input,
        CancellationToken cancellationToken)
    {
        var commandLine = CommandLine.Parse(input);
        if (string.IsNullOrWhiteSpace(commandLine.Text))
        {
            return;
        }

        try
        {
            if (!commandLine.IsCommand)
            {
                await _session.SendAsync(commandLine.Text, cancellationToken)
                    .ConfigureAwait(false);
                _terminal.WriteOwnMessage(
                    _session.ActiveConversation!,
                    commandLine.Text);
                return;
            }

            if (!_commands.TryGetValue(commandLine.Name, out var command))
            {
                _terminal.WriteWarning(
                    $"Unknown command /{commandLine.Name}. Use /help to list commands.");
                return;
            }

            await command.ExecuteAsync(
                    commandLine.Arguments,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or System.Net.Sockets.SocketException
                or System.Security.Cryptography.CryptographicException)
        {
            _terminal.WriteError(exception.Message);
        }
    }
}
