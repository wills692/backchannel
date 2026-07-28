using BackChannel.App.Runtime;
using BackChannel.Core.Peers;

namespace BackChannel.App.Ui;

public interface IBackChannelTerminal
{
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    Task<Peer?> SelectPeerAsync(
        IReadOnlyList<Peer> peers,
        CancellationToken cancellationToken);

    void ShowBanner(BackChannelNode node);

    void ShowPeers(IReadOnlyList<Peer> peers);

    void ShowCommands(IReadOnlyList<(string Name, string Description)> commands);

    void WriteInfo(string message);

    void WriteWarning(string message);

    void WriteError(string message);

    void WriteIncomingMessage(Peer sender, string plaintext);

    void WriteOwnMessage(string plaintext);
}
