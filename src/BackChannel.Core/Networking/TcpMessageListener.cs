using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Networking;

public sealed class TcpMessageListener : IAsyncDisposable
{
    private readonly ChannelWriter<InboundEvent> _events;
    private readonly TcpListenerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Lock _clientTasksLock = new();
    private readonly HashSet<Task> _clientTasks = [];

    private TcpListener? _listener;
    private SemaphoreSlim? _connectionSlots;
    private CancellationTokenSource? _stopCts;
    private Task? _acceptTask;

    public TcpMessageListener(
        ChannelWriter<InboundEvent> events,
        TcpListenerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        _events = events;
        _options = options ?? new TcpListenerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions(_options);
    }

    public int LocalPort
    {
        get
        {
            var endpoint = _listener?.LocalEndpoint as IPEndPoint;
            return endpoint?.Port
                   ?? throw new InvalidOperationException("The TCP listener is not running.");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_listener is not null)
            {
                throw new InvalidOperationException("The TCP listener is already running.");
            }

            var listener = new TcpListener(
                _options.ListenAddress,
                _options.ListenPort);
            var connectionSlots = new SemaphoreSlim(
                _options.MaximumConcurrentConnections,
                _options.MaximumConcurrentConnections);
            var stopCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                listener.Start();
                _listener = listener;
                _connectionSlots = connectionSlots;
                _stopCts = stopCts;
                _acceptTask = AcceptLoopAsync(
                    listener,
                    connectionSlots,
                    stopCts.Token);
            }
            catch
            {
                listener.Stop();
                connectionSlots.Dispose();
                stopCts.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_listener is null)
            {
                return;
            }

            var listener = _listener;
            var connectionSlots = _connectionSlots;
            var stopCts = _stopCts;
            var acceptTask = _acceptTask;

            _listener = null;
            _connectionSlots = null;
            _stopCts = null;
            _acceptTask = null;

            stopCts?.Cancel();
            listener.Stop();

            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopCts?.IsCancellationRequested == true)
                {
                }
            }

            Task[] clientTasks;
            lock (_clientTasksLock)
            {
                clientTasks = [.. _clientTasks];
            }

            if (clientTasks.Length > 0)
            {
                await Task.WhenAll(clientTasks)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            connectionSlots?.Dispose();
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

    private async Task AcceptLoopAsync(
        TcpListener listener,
        SemaphoreSlim connectionSlots,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            TcpClient? client = null;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                client.NoDelay = true;

                var clientTask = RunClientAsync(
                    client,
                    connectionSlots,
                    cancellationToken);
                client = null;

                lock (_clientTasksLock)
                {
                    _clientTasks.Add(clientTask);
                }

                _ = RemoveCompletedClientAsync(clientTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                connectionSlots.Release();
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                connectionSlots.Release();
                client?.Dispose();
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                connectionSlots.Release();
                client?.Dispose();
                break;
            }
            catch (Exception exception)
            {
                connectionSlots.Release();
                client?.Dispose();
                await PublishFaultAsync(
                        exception,
                        remoteEndpoint: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task RunClientAsync(
        TcpClient client,
        SemaphoreSlim connectionSlots,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionSlots.Release();
        }
    }

    private async Task RemoveCompletedClientAsync(Task clientTask)
    {
        try
        {
            await clientTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            lock (_clientTasksLock)
            {
                _clientTasks.Remove(clientTask);
            }
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;

            try
            {
                await using var stream = client.GetStream();

                while (!cancellationToken.IsCancellationRequested)
                {
                    var envelope = await TcpFrameCodec.ReadAsync(stream, cancellationToken)
                        .ConfigureAwait(false);

                    if (envelope is null)
                    {
                        break;
                    }

                    if (envelope.ProtocolVersion != Envelope.CurrentProtocolVersion)
                    {
                        throw new InvalidDataException(
                            $"Unsupported protocol version {envelope.ProtocolVersion}.");
                    }

                    if (envelope is not ChatMessage chatMessage)
                    {
                        throw new InvalidDataException(
                            $"Envelope type {envelope.GetType().Name} is not valid on the TCP chat transport.");
                    }

                    await _events.WriteAsync(
                            new ChatMessageReceivedEvent(
                                chatMessage,
                                remoteEndpoint
                                ?? new IPEndPoint(IPAddress.None, IPEndPoint.MinPort),
                                _timeProvider.GetUtcNow()),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                await PublishFaultAsync(
                        exception,
                        remoteEndpoint,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
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
                        nameof(TcpMessageListener),
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

    private static void ValidateOptions(TcpListenerOptions options)
    {
        if (options.ListenPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ListenPort,
                "A TCP listen port must be between 0 and 65535.");
        }

        if (options.MaximumConcurrentConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumConcurrentConnections,
                "Maximum concurrent connections must be positive.");
        }
    }
}
