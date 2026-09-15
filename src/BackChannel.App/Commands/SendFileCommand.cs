using BackChannel.App.Runtime;
using BackChannel.App.Ui;
using BackChannel.Core.Peers;

namespace BackChannel.App.Commands;

public sealed class SendFileCommand(
    BackChannelNode node,
    FileTransferService transfers,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "send-file";

    public string Description => "Offer a file using /send-file [peer] <path>.";

    public async Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arguments);

        var peers = node.Peers.Snapshot();
        if (peers.Count == 0)
        {
            terminal.WriteWarning(
                "No peers are known. Ask both nodes to run /hello.");
            return;
        }

        var (selector, filePath) = SplitArguments(arguments);
        Peer? peer;
        if (selector is null)
        {
            peer = await terminal.SelectPeerAsync(peers, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var matches = MessageCommand.FindMatches(peers, selector);
            if (matches.Count != 1)
            {
                terminal.WriteWarning(
                    matches.Count == 0
                        ? $"No peer matches '{selector}'."
                        : $"'{selector}' is ambiguous; use more of the fingerprint.");
                return;
            }

            peer = matches[0];
        }

        if (peer is null)
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            terminal.WriteWarning($"The file '{filePath}' does not exist.");
            return;
        }

        await transfers.QueueAsync(peer, filePath, cancellationToken).ConfigureAwait(false);
        terminal.WriteInfo($"Offered {Path.GetFileName(filePath)} to {peer.DisplayName}.");
    }

    private static (string? Selector, string FilePath) SplitArguments(string arguments)
    {
        if (File.Exists(arguments))
        {
            return (null, arguments);
        }

        var separator = arguments.IndexOf(' ');
        if (separator < 0)
        {
            return (null, arguments);
        }

        return (arguments[..separator], arguments[(separator + 1)..].Trim());
    }
}
