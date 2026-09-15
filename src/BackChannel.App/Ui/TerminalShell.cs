using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text.Json;
using BackChannel.App.Commands;
using BackChannel.App.Runtime;
using BackChannel.Core.Crypto;
using BackChannel.Core.Networking;
using BackChannel.Core.Peers;
using BackChannel.Core.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackChannel.App.Ui;

public sealed class TerminalShell(
    BackChannelNode node,
    ChatSession session,
    FileTransferService transfers,
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
        PendingFileOffer? pendingFileOffer = null;
        Task? offerTimeoutTask = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            inputTask ??= terminal.ReadLineAsync(stoppingToken);
            var eventTask = node.Events.WaitToReadAsync(stoppingToken).AsTask();
            var completed = offerTimeoutTask is null
                ? await Task.WhenAny(inputTask, eventTask).ConfigureAwait(false)
                : await Task.WhenAny(inputTask, eventTask, offerTimeoutTask)
                .ConfigureAwait(false);

            if (completed == offerTimeoutTask)
            {
                pendingFileOffer = null;
                offerTimeoutTask = null;
                continue;
            }

            if (completed == inputTask)
            {
                var line = await inputTask.ConfigureAwait(false);
                inputTask = null;

                if (line is null)
                {
                    applicationLifetime.StopApplication();
                    break;
                }

                if (pendingFileOffer is not null)
                {
                    try
                    {
                        if (await ProcessFileOfferResponseAsync(
                                pendingFileOffer,
                                line,
                                stoppingToken)
                            .ConfigureAwait(false))
                        {
                            pendingFileOffer = null;
                            offerTimeoutTask = null;
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException
                            or InvalidDataException
                            or InvalidOperationException
                            or SocketException)
                    {
                        terminal.WriteError(exception.Message);
                        pendingFileOffer = null;
                        offerTimeoutTask = null;
                    }

                    continue;
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
                    var offer = await ProcessInboundEventAsync(
                            inboundEvent,
                            pendingFileOffer,
                            stoppingToken)
                        .ConfigureAwait(false);
                    if (offer is not null)
                    {
                        pendingFileOffer = offer;
                        offerTimeoutTask = Task.Delay(
                            TimeSpan.FromSeconds(60),
                            stoppingToken);
                    }
                }
            }
        }
    }

    private async Task<PendingFileOffer?> ProcessInboundEventAsync(
        InboundEvent inboundEvent,
        PendingFileOffer? pendingFileOffer,
        CancellationToken cancellationToken)
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
                return await ProcessChatMessageAsync(
                        chat,
                        pendingFileOffer,
                        cancellationToken)
                    .ConfigureAwait(false);

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

        return null;
    }

    private async Task<PendingFileOffer?> ProcessChatMessageAsync(
        ChatMessageReceivedEvent chat,
        PendingFileOffer? pendingFileOffer,
        CancellationToken cancellationToken)
    {
        try
        {
            var fingerprint = new Fingerprint(chat.Message.SenderFingerprint);
            if (!node.Peers.TryGet(fingerprint, out var sender) || sender is null)
            {
                terminal.WriteWarning(
                    $"Ignored a message from unknown key {fingerprint.Value[..12]}.");
                return null;
            }

            var plaintext = node.DecryptMessage(chat.Message, sender);
            switch (chat.Message.ContentType)
            {
                case ChatMessage.ContentTypes.Chat:
                {
                    var conversation = session.Receive(chat.Message, sender);
                    terminal.WriteIncomingMessage(conversation, sender, plaintext);
                    break;
                }

                case ChatMessage.ContentTypes.FileOffer:
                {
                    var offer = JsonSerializer.Deserialize<FileOfferPayload>(plaintext)
                        ?? throw new InvalidDataException("The file offer payload was empty.");
                    if (pendingFileOffer is not null)
                    {
                        await transfers.DeclineOfferAsync(offer, sender, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    }

                    terminal.WriteInfo(
                        $"{sender.DisplayName} offers {offer.FileName} ({offer.Length} bytes). Accept? [Y/N]");
                    return new PendingFileOffer(offer, sender);
                }

                case ChatMessage.ContentTypes.FileResponse:
                {
                    var response = JsonSerializer.Deserialize<FileResponsePayload>(plaintext)
                        ?? throw new InvalidDataException("The file response payload was empty.");
                    transfers.ReceiveResponse(response, sender);
                    break;
                }

                case ChatMessage.ContentTypes.FileChunk:
                {
                    var chunk = JsonSerializer.Deserialize<FileChunkPayload>(plaintext)
                        ?? throw new InvalidDataException("The file chunk payload was empty.");
                    var completedPath = await transfers.ReceiveChunkAsync(
                            chunk,
                            sender,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (completedPath is not null)
                    {
                        terminal.WriteInfo($"Received {Path.GetFileName(completedPath)}.");
                    }

                    break;
                }

                default:
                    throw new InvalidDataException("The encrypted message has an unsupported content type.");
            }
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or InvalidDataException
                or FormatException
                or ArgumentException
                or JsonException)
        {
            ChatMessageRejected(logger, chat.RemoteEndpoint, exception);
            terminal.WriteWarning("Rejected an invalid encrypted message.");
        }

        return null;
    }

    private async Task<bool> ProcessFileOfferResponseAsync(
        PendingFileOffer pendingFileOffer,
        string input,
        CancellationToken cancellationToken)
    {
        if (string.Equals(input.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
        {
            await transfers.AcceptOfferAsync(
                    pendingFileOffer.Offer,
                    pendingFileOffer.Sender,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (string.Equals(input.Trim(), "N", StringComparison.OrdinalIgnoreCase))
        {
            await transfers.DeclineOfferAsync(
                    pendingFileOffer.Offer,
                    pendingFileOffer.Sender,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        terminal.WriteWarning("Enter Y to accept or N to decline the offered file.");
        return false;
    }

    private sealed record PendingFileOffer(FileOfferPayload Offer, Peer Sender);
}
