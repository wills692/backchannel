using BackChannel.App.Runtime;
using BackChannel.Core.Conversations;
using BackChannel.Core.Peers;
using Spectre.Console;
using System.Globalization;

namespace BackChannel.App.Ui;

public sealed class SpectreBackChannelTerminal : IBackChannelTerminal
{
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var prompt = new TextPrompt<string>("[grey]> [/]")
            .AllowEmpty();
        return await AnsiConsole.PromptAsync(prompt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Peer?> SelectPeerAsync(
        IReadOnlyList<Peer> peers,
        CancellationToken cancellationToken)
    {
        if (peers.Count == 0)
        {
            return null;
        }

        var prompt = new SelectionPrompt<Peer>()
            .Title("Choose a peer")
            .UseConverter(
                static peer =>
                    $"{peer.DisplayName} ({peer.MachineName}, {peer.Fingerprint.Value[..12]})")
            .AddChoices(peers);

        return await AnsiConsole.PromptAsync(prompt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Peer>> SelectPeersAsync(
        IReadOnlyList<Peer> peers,
        CancellationToken cancellationToken)
    {
        var prompt = new MultiSelectionPrompt<Peer>()
            .Title("Choose at least two peers")
            .Required()
            .PageSize(12)
            .InstructionsText(
                "[grey](Press [blue]<space>[/] to select and [green]<enter>[/] to accept.)[/]")
            .UseConverter(
                static peer =>
                    $"{peer.DisplayName} ({peer.MachineName}, {peer.Fingerprint.Value[..12]})")
            .AddChoices(peers);

        return await AnsiConsole.PromptAsync(prompt, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string> ReadGroupNameAsync(CancellationToken cancellationToken)
    {
        var prompt = new TextPrompt<string>("Group name:");
        return AnsiConsole.PromptAsync(prompt, cancellationToken);
    }

    public void ShowBanner(BackChannelNode node)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("[bold deepskyblue1]BackChannel[/]", "[grey]LAN messenger[/]");
        grid.AddRow("Name", Escape(node.DisplayName));
        grid.AddRow("Fingerprint", $"[grey]{node.Fingerprint.Value}[/]");
        grid.AddRow(
            "TCP listener",
            node.TcpPort.ToString(CultureInfo.InvariantCulture));
        AnsiConsole.Write(grid);
        AnsiConsole.MarkupLine(
            "[grey]Use /hello to discover peers and /help for commands.[/]");
    }

    public void ShowPeers(IReadOnlyList<Peer> peers)
    {
        if (peers.Count == 0)
        {
            WriteInfo("No peers discovered.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Display name")
            .AddColumn("Machine")
            .AddColumn("Address")
            .AddColumn("Fingerprint");

        foreach (var peer in peers)
        {
            table.AddRow(
                Escape(peer.DisplayName),
                Escape(peer.MachineName),
                Escape(peer.TcpEndpoint.ToString()),
                $"[grey]{peer.Fingerprint.Value[..12]}[/]");
        }

        AnsiConsole.Write(table);
    }

    public void ShowCommands(
        IReadOnlyList<(string Name, string Description)> commands)
    {
        var table = new Table()
            .Border(TableBorder.Simple)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        foreach (var command in commands)
        {
            table.AddRow(
                $"[deepskyblue1]/{Escape(command.Name)}[/]",
                Escape(command.Description));
        }

        AnsiConsole.Write(table);
    }

    public void WriteInfo(string message) =>
        AnsiConsole.MarkupLine($"[grey]{Escape(message)}[/]");

    public void WriteWarning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]{Escape(message)}[/]");

    public void WriteError(string message) =>
        AnsiConsole.MarkupLine($"[red]{Escape(message)}[/]");

    public void WriteIncomingMessage(
        Conversation conversation,
        Peer sender,
        string plaintext)
    {
        var prefix = conversation.Members.Count > 1
            ? $"[mediumpurple1][[{Escape(conversation.Name)}]][/] "
            : string.Empty;
        AnsiConsole.MarkupLine(
            $"{prefix}[deepskyblue1]{Escape(sender.DisplayName)}:[/] {Escape(plaintext)}");
    }

    public void WriteOwnMessage(Conversation conversation, string plaintext)
    {
        var prefix = conversation.Members.Count > 1
            ? $"[mediumpurple1][[{Escape(conversation.Name)}]][/] "
            : string.Empty;
        AnsiConsole.MarkupLine(
            $"{prefix}[green]you:[/] {Escape(plaintext)}");
    }

    private static string Escape(string value) => Markup.Escape(value);
}
