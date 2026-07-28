using BackChannel.App.Runtime;
using BackChannel.App.Ui;
using BackChannel.Core.Peers;

namespace BackChannel.App.Commands;

public sealed class MessageCommand(
    BackChannelNode node,
    ChatSession session,
    IBackChannelTerminal terminal) : IBackChannelCommand
{
    public string Name => "msg";

    public string Description => "Select a peer, optionally using /msg <name> [message].";

    public async Task ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var peers = node.Peers.Snapshot();
        if (peers.Count == 0)
        {
            terminal.WriteWarning(
                "No peers are known. Ask both nodes to run /hello.");
            return;
        }

        var (selector, message) = SplitArguments(arguments);
        Peer? peer;

        if (string.IsNullOrWhiteSpace(selector))
        {
            peer = await terminal.SelectPeerAsync(peers, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var matches = FindMatches(peers, selector);
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

        session.Activate(peer);
        terminal.WriteInfo($"Active conversation: {peer.DisplayName}.");

        if (!string.IsNullOrWhiteSpace(message))
        {
            await session.SendAsync(message, cancellationToken).ConfigureAwait(false);
            terminal.WriteOwnMessage(message);
        }
    }

    internal static IReadOnlyList<Peer> FindMatches(
        IReadOnlyList<Peer> peers,
        string selector) =>
        peers.Where(
                peer =>
                    string.Equals(
                        peer.DisplayName,
                        selector,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        peer.MachineName,
                        selector,
                        StringComparison.OrdinalIgnoreCase)
                    || peer.Fingerprint.Value.StartsWith(
                        selector,
                        StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static (string Selector, string Message) SplitArguments(
        string arguments)
    {
        var separator = arguments.IndexOf(' ');
        return separator < 0
            ? (arguments, string.Empty)
            : (arguments[..separator], arguments[(separator + 1)..].Trim());
    }
}
