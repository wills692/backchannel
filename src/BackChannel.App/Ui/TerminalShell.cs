using System.Security.Cryptography;
using BackChannel.App.Commands;
using BackChannel.App.Runtime;
using BackChannel.Core.Crypto;
using BackChannel.Core.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackChannel.App.Ui;

public sealed class TerminalShell(
    BackChannelNode node,
    ChatSession session,
    CommandDispatcher dispatcher,
    IBackChannelTerminal terminal,
    IHostApplicationLifetime applicationLifetime,
    ILogger<TerminalShell> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, object?, Exception?>
        NetworkInputRejected = LoggerMessage.Define<string, object?>(
            LogLevel.Warning,
            new EventId(1, nameof(NetworkInputRejected)),
            "{Component} rejected network input from {RemoteEndpoint}.");

    private static readonly Action<ILogger, object?, Exception?>
        ChatMessageRejected = LoggerMessage.Define<object?>(
            LogLevel.Warning,
            new EventId(2, nameof(ChatMessageRejected)),
            "Rejected a chat message from {RemoteEndpoint}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        terminal.ShowBanner(node);

        Task<string?>? inputTask = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            inputTask ??= terminal.ReadLineAsync(stoppingToken);
            var eventTask = node.Events.WaitToReadAsync(stoppingToken).AsTask();
            var completed = await Task.WhenAny(inputTask, eventTask)
                .ConfigureAwait(false);

            if (completed == inputTask)
            {
                var line = await inputTask.ConfigureAwait(false);
                inputTask = null;

                if (line is null)
                {
                    applicationLifetime.StopApplication();
                    break;
                }

                await dispatcher.DispatchAsync(line, stoppingToken)
                    .ConfigureAwait(false);

                if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    break;
                }
            }
            else if (await eventTask.ConfigureAwait(false))
            {
                while (node.Events.TryRead(out var inboundEvent))
                {
                    ProcessInboundEvent(inboundEvent);
                }
            }
        }
    }

    private void ProcessInboundEvent(InboundEvent inboundEvent)
    {
        switch (inboundEvent)
        {
            case PeerDiscoveredEvent
            {
                Change: Core.Peers.PeerRegistrationChange.Added,
            } discovered:
                terminal.WriteInfo(
                    $"Discovered {discovered.Peer.DisplayName} at {discovered.Peer.TcpEndpoint}.");
                break;

            case PeerDiscoveredEvent
            {
                Change: Core.Peers.PeerRegistrationChange.Updated,
            } discovered:
                terminal.WriteInfo(
                    $"Updated {discovered.Peer.DisplayName} at {discovered.Peer.TcpEndpoint}.");
                break;

            case PeerDepartedEvent departed:
                var activeConversationClosed =
                    session.RemovePeer(departed.Fingerprint);
                terminal.WriteInfo(
                    $"Peer {departed.Fingerprint.Value[..12]} left.");
                if (activeConversationClosed)
                {
                    terminal.WriteWarning(
                        "The active conversation closed because a member left.");
                }
                break;

            case ChatMessageReceivedEvent chat:
                ProcessChatMessage(chat);
                break;

            case NetworkFaultEvent fault:
                NetworkInputRejected(
                    logger,
                    fault.Component,
                    fault.RemoteEndpoint,
                    fault.Error);
                terminal.WriteWarning(
                    $"{fault.Component}: {fault.Error.Message}");
                break;
        }
    }

    private void ProcessChatMessage(ChatMessageReceivedEvent chat)
    {
        try
        {
            var fingerprint = new Fingerprint(chat.Message.SenderFingerprint);
            if (!node.Peers.TryGet(fingerprint, out var sender) || sender is null)
            {
                terminal.WriteWarning(
                    $"Ignored a message from unknown key {fingerprint.Value[..12]}.");
                return;
            }

            var plaintext = node.DecryptMessage(chat.Message, sender);
            var conversation = session.Receive(chat.Message, sender);
            terminal.WriteIncomingMessage(conversation, sender, plaintext);
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or InvalidDataException
                or FormatException
                or ArgumentException)
        {
            ChatMessageRejected(logger, chat.RemoteEndpoint, exception);
            terminal.WriteWarning("Rejected an invalid encrypted message.");
        }
    }
}
