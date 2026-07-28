using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using BackChannel.App.Configuration;
using BackChannel.Core.Conversations;
using BackChannel.Core.Crypto;
using BackChannel.Core.Networking;
using BackChannel.Core.Peers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackChannel.App.Runtime;

public sealed class BackChannelNode : IHostedService, IAsyncDisposable
{
    private static readonly Action<ILogger, string, string, int, int, Exception?>
        NodeStarted = LoggerMessage.Define<string, string, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(NodeStarted)),
            "Node {DisplayName} ({Fingerprint}) is listening on TCP {TcpPort} and UDP {DiscoveryPort}.");

    private static readonly Action<ILogger, Exception?> GoodbyeFailed =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(2, nameof(GoodbyeFailed)),
            "Could not broadcast the shutdown notice.");

    private readonly BackChannelOptions _options;
    private readonly ILogger<BackChannelNode> _logger;
    private readonly Channel<InboundEvent> _events =
        Channel.CreateUnbounded<InboundEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

    private readonly IdentityKeyPair _identity = IdentityKeyPair.Create();

    private TcpMessageListener? _tcpListener;
    private UdpDiscoveryService? _discovery;
    private bool _disposed;

    public BackChannelNode(
        IOptions<BackChannelOptions> options,
        ILogger<BackChannelNode> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    public PeerRegistry Peers { get; } = new();

    public ConversationRegistry Conversations { get; } = new();

    public ChannelReader<InboundEvent> Events => _events.Reader;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName)
            ? Environment.UserName
            : _options.DisplayName.Trim();

    public string MachineName =>
        string.IsNullOrWhiteSpace(_options.MachineName)
            ? Environment.MachineName
            : _options.MachineName.Trim();

    public Fingerprint Fingerprint => _identity.Fingerprint;

    public int TcpPort =>
        _tcpListener?.LocalPort
        ?? throw new InvalidOperationException("The BackChannel node is not running.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcpListener = new TcpMessageListener(
            _events.Writer,
            new TcpListenerOptions
            {
                ListenAddress = IPAddress.Any,
                ListenPort = _options.TcpPort,
                MaximumConcurrentConnections =
                    _options.MaximumConcurrentConnections,
            });

        try
        {
            await tcpListener.StartAsync(cancellationToken).ConfigureAwait(false);

            var localNode = new LocalNode(
                MachineName,
                DisplayName,
                _identity.PublicIdentity,
                tcpListener.LocalPort);
            _ = BackChannelOptions.TryGetDiscoveryTarget(
                _options,
                out var discoveryTarget);
            var discovery = new UdpDiscoveryService(
                localNode,
                Peers,
                _events.Writer,
                new UdpDiscoveryOptions
                {
                    ListenAddress = IPAddress.Any,
                    ListenPort = _options.DiscoveryPort,
                    DefaultTarget = discoveryTarget,
                    ReuseAddress = true,
                });

            try
            {
                await discovery.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await discovery.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _tcpListener = tcpListener;
            _discovery = discovery;

            NodeStarted(
                _logger,
                DisplayName,
                Fingerprint.Value,
                TcpPort,
                _options.DiscoveryPort,
                null);
        }
        catch
        {
            await tcpListener.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task BroadcastHelloAsync(CancellationToken cancellationToken = default) =>
        GetDiscovery().SendHelloAsync(cancellationToken: cancellationToken);

    public async Task SendMessageAsync(
        Conversation conversation,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var recipients = new List<Peer>(conversation.Members.Count);
        foreach (var fingerprint in conversation.Members)
        {
            if (!Peers.TryGet(fingerprint, out var peer) || peer is null)
            {
                throw new InvalidOperationException(
                    $"Conversation member {fingerprint.Value[..12]} is no longer available.");
            }

            recipients.Add(peer);
        }

        var message = HybridCipher.Encrypt(
            plaintext,
            conversation.Id,
            _identity,
            recipients.Select(static peer => peer.Identity));

        await Task.WhenAll(
                recipients.Select(
                    peer => TcpMessageSender.SendAsync(
                        peer.TcpEndpoint,
                        message,
                        cancellationToken)))
            .ConfigureAwait(false);
    }

    public string DecryptMessage(
        Core.Protocol.ChatMessage message,
        Peer sender)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        return HybridCipher.Decrypt(message, sender.Identity, _identity);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var discovery = _discovery;
        var tcpListener = _tcpListener;
        _discovery = null;
        _tcpListener = null;

        if (discovery is not null)
        {
            try
            {
                await discovery.SendGoodbyeAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or SocketException or OperationCanceledException)
            {
                GoodbyeFailed(_logger, exception);
            }

            await discovery.DisposeAsync().ConfigureAwait(false);
        }

        if (tcpListener is not null)
        {
            await tcpListener.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _events.Writer.TryComplete();
        _identity.Dispose();
        _disposed = true;
    }

    private UdpDiscoveryService GetDiscovery() =>
        _discovery
        ?? throw new InvalidOperationException("The BackChannel node is not running.");
}
