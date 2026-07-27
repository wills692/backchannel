using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Networking;

public sealed class UdpDiscoveryService : IAsyncDisposable
{
    private const int MaximumNameLength = 128;
    private const int MaximumDatagramLength = 16 * 1024;

    private readonly LocalNode _localNode;
    private readonly PeerRegistry _peerRegistry;
    private readonly ChannelWriter<InboundEvent> _events;
    private readonly UdpDiscoveryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private UdpClient? _client;
    private CancellationTokenSource? _stopCts;
    private Task? _receiveTask;

    public UdpDiscoveryService(
        LocalNode localNode,
        PeerRegistry peerRegistry,
        ChannelWriter<InboundEvent> events,
        UdpDiscoveryOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localNode);
        ArgumentNullException.ThrowIfNull(peerRegistry);
        ArgumentNullException.ThrowIfNull(events);

        _localNode = localNode;
        _peerRegistry = peerRegistry;
        _events = events;
        _options = options ?? new UdpDiscoveryOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions(_options);
    }

    public int LocalPort
    {
        get
        {
            var endpoint = _client?.Client.LocalEndPoint as IPEndPoint;
            return endpoint?.Port
                   ?? throw new InvalidOperationException("UDP discovery is not running.");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_client is not null)
            {
                throw new InvalidOperationException("UDP discovery is already running.");
            }

            var client = new UdpClient(AddressFamily.InterNetwork);

            try
            {
                client.Client.ExclusiveAddressUse = false;
                if (_options.ReuseAddress)
                {
                    client.Client.SetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.ReuseAddress,
                        optionValue: true);
                }

                client.EnableBroadcast = true;
                client.Client.Bind(
                    new IPEndPoint(_options.ListenAddress, _options.ListenPort));

                var stopCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _client = client;
                _stopCts = stopCts;
                _receiveTask = ReceiveLoopAsync(client, stopCts.Token);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task SendHelloAsync(
        IPEndPoint? target = null,
        CancellationToken cancellationToken = default)
    {
        var hello = new HelloMessage
        {
            MachineName = _localNode.MachineName,
            DisplayName = _localNode.DisplayName,
            PublicKey = _localNode.Identity.EncodedKey,
            TcpPort = _localNode.TcpPort,
        };

        return SendDiscoveryMessageAsync(
            hello,
            ResolveTarget(target),
            cancellationToken);
    }

    public Task SendGoodbyeAsync(
        IPEndPoint? target = null,
        CancellationToken cancellationToken = default)
    {
        var goodbye = new GoodbyeMessage
        {
            SenderFingerprint = _localNode.Identity.Fingerprint.Value,
        };

        return SendDiscoveryMessageAsync(
            goodbye,
            ResolveTarget(target),
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_client is null)
            {
                return;
            }

            var client = _client;
            var stopCts = _stopCts;
            var receiveTask = _receiveTask;

            _client = null;
            _stopCts = null;
            _receiveTask = null;

            stopCts?.Cancel();
            client.Dispose();

            if (receiveTask is not null)
            {
                try
                {
                    await receiveTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopCts?.IsCancellationRequested == true)
                {
                }
            }

            stopCts?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult datagram;

            try
            {
                datagram = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await PublishFaultAsync(
                        exception,
                        remoteEndpoint: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            }

            try
            {
                await ProcessDatagramAsync(datagram, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await PublishFaultAsync(
                        exception,
                        datagram.RemoteEndPoint,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessDatagramAsync(
        UdpReceiveResult datagram,
        CancellationToken cancellationToken)
    {
        if (datagram.Buffer.Length > MaximumDatagramLength)
        {
            throw new InvalidDataException(
                $"Discovery datagrams cannot exceed {MaximumDatagramLength} bytes.");
        }

        var envelope = JsonSerializer.Deserialize(
                           datagram.Buffer,
                           BackChannelJsonContext.Default.Envelope)
                       ?? throw new InvalidDataException(
                           "The discovery datagram contained a null envelope.");

        if (envelope.ProtocolVersion != Envelope.CurrentProtocolVersion)
        {
            throw new InvalidDataException(
                $"Unsupported protocol version {envelope.ProtocolVersion}.");
        }

        switch (envelope)
        {
            case HelloMessage hello:
                var isRemotePeer = await RegisterPeerAsync(
                        hello.MachineName,
                        hello.DisplayName,
                        hello.PublicKey,
                        hello.TcpPort,
                        datagram.RemoteEndPoint.Address,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!isRemotePeer)
                {
                    break;
                }

                await SendAnnounceAsync(datagram.RemoteEndPoint, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case AnnounceMessage announce:
                await RegisterPeerAsync(
                        announce.MachineName,
                        announce.DisplayName,
                        announce.PublicKey,
                        announce.TcpPort,
                        datagram.RemoteEndPoint.Address,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case GoodbyeMessage goodbye:
                var fingerprint = new Fingerprint(goodbye.SenderFingerprint);
                if (fingerprint != _localNode.Identity.Fingerprint
                    && _peerRegistry.Remove(fingerprint, out _))
                {
                    await _events.WriteAsync(
                            new PeerDepartedEvent(
                                fingerprint,
                                _timeProvider.GetUtcNow()),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                break;

            default:
                throw new InvalidDataException(
                    $"Envelope type {envelope.GetType().Name} is not valid for UDP discovery.");
        }
    }

    private async Task<bool> RegisterPeerAsync(
        string machineName,
        string displayName,
        string encodedPublicKey,
        int tcpPort,
        IPAddress sourceAddress,
        CancellationToken cancellationToken)
    {
        ValidateName(machineName, nameof(machineName));
        ValidateName(displayName, nameof(displayName));

        if (tcpPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new InvalidDataException(
                $"Advertised TCP port {tcpPort} must be between 1 and 65535.");
        }

        var identity = PublicIdentity.Parse(encodedPublicKey);
        if (identity.Fingerprint == _localNode.Identity.Fingerprint)
        {
            return false;
        }

        var peer = new Peer(
            machineName,
            displayName,
            identity,
            new IPEndPoint(sourceAddress, tcpPort),
            _timeProvider.GetUtcNow());
        var change = _peerRegistry.Upsert(peer);

        await _events.WriteAsync(
                new PeerDiscoveredEvent(peer, change, _timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private Task SendAnnounceAsync(
        IPEndPoint target,
        CancellationToken cancellationToken)
    {
        var announce = new AnnounceMessage
        {
            MachineName = _localNode.MachineName,
            DisplayName = _localNode.DisplayName,
            PublicKey = _localNode.Identity.EncodedKey,
            TcpPort = _localNode.TcpPort,
        };

        return SendDiscoveryMessageAsync(announce, target, cancellationToken);
    }

    private async Task SendDiscoveryMessageAsync(
        Envelope envelope,
        IPEndPoint target,
        CancellationToken cancellationToken)
    {
        var client = _client
                     ?? throw new InvalidOperationException(
                         "UDP discovery must be started before sending.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BackChannelJsonContext.Default.Envelope);

        await client.SendAsync(payload, target, cancellationToken).ConfigureAwait(false);
    }

    private IPEndPoint ResolveTarget(IPEndPoint? target)
    {
        if (target is not null)
        {
            return target;
        }

        if (_options.DefaultTarget is not null)
        {
            return _options.DefaultTarget;
        }

        if (_options.ListenPort == 0)
        {
            throw new InvalidOperationException(
                "An explicit discovery target is required when listening on an ephemeral port.");
        }

        return new IPEndPoint(IPAddress.Broadcast, _options.ListenPort);
    }

    private async Task PublishFaultAsync(
        Exception exception,
        IPEndPoint? remoteEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            await _events.WriteAsync(
                    new NetworkFaultEvent(
                        nameof(UdpDiscoveryService),
                        exception,
                        remoteEndpoint,
                        _timeProvider.GetUtcNow()),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void ValidateOptions(UdpDiscoveryOptions options)
    {
        if (options.ListenAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "UDP broadcast discovery currently supports IPv4 only.",
                nameof(options));
        }

        if (options.ListenPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ListenPort,
                "A UDP listen port must be between 0 and 65535.");
        }
    }

    private static void ValidateName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumNameLength)
        {
            throw new InvalidDataException(
                $"{fieldName} must contain between 1 and {MaximumNameLength} characters.");
        }
    }
}
