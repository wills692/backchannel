using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using BackChannel.Core.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackChannel.App.Runtime;

public sealed class FileTransferService(
    BackChannelNode node,
    ILogger<FileTransferService> logger) : BackgroundService
{
    private const int ChunkSize = 256 * 1024;
    private static readonly TimeSpan OfferResponseTimeout = TimeSpan.FromSeconds(60);
    private readonly Channel<OutboundTransferRequest> _outboundTransfers =
        Channel.CreateUnbounded<OutboundTransferRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
    private readonly ConcurrentDictionary<Guid, PendingOutboundOffer> _pendingOffers = [];
    private readonly ConcurrentDictionary<Guid, IncomingTransfer> _incomingTransfers = [];

    private static readonly Action<ILogger, string, string, Exception?> TransferFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(TransferFailed)),
            "File transfer {TransferId} for {FilePath} did not complete.");

    public ValueTask QueueAsync(
        Peer recipient,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected file does not exist.", fullPath);
        }

        return _outboundTransfers.Writer.WriteAsync(
            new OutboundTransferRequest(recipient, fullPath),
            cancellationToken);
    }

    public async Task AcceptOfferAsync(
        FileOfferPayload offer,
        Peer sender,
        CancellationToken cancellationToken)
    {
        ValidateOffer(offer);
        ArgumentNullException.ThrowIfNull(sender);

        var incoming = IncomingTransfer.Create(offer, sender.Fingerprint);
        if (!_incomingTransfers.TryAdd(offer.TransferId, incoming))
        {
            await incoming.DisposeAsync(deleteTemporaryFile: true).ConfigureAwait(false);
            throw new InvalidOperationException("A transfer with this identifier is already pending.");
        }

        try
        {
            await SendResponseAsync(offer.TransferId, accepted: true, sender, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (_incomingTransfers.TryRemove(offer.TransferId, out var pending))
            {
                await pending.DisposeAsync(deleteTemporaryFile: true).ConfigureAwait(false);
            }

            throw;
        }
    }

    public Task DeclineOfferAsync(
        FileOfferPayload offer,
        Peer sender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(sender);

        return SendResponseAsync(offer.TransferId, accepted: false, sender, cancellationToken);
    }

    public void ReceiveResponse(FileResponsePayload response, Peer sender)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(sender);

        if (_pendingOffers.TryGetValue(response.TransferId, out var pending)
            && pending.RecipientFingerprint == sender.Fingerprint)
        {
            pending.Response.TrySetResult(response.Accepted);
        }
    }

    public async Task<string?> ReceiveChunkAsync(
        FileChunkPayload chunk,
        Peer sender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(sender);

        if (!_incomingTransfers.TryGetValue(chunk.TransferId, out var incoming))
        {
            throw new InvalidDataException("The file chunk does not belong to an accepted transfer.");
        }

        if (incoming.SenderFingerprint != sender.Fingerprint)
        {
            throw new InvalidDataException("The file chunk sender does not match the accepted offer.");
        }

        try
        {
            var completedPath = await incoming.WriteChunkAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (completedPath is not null)
            {
                _incomingTransfers.TryRemove(chunk.TransferId, out _);
            }

            return completedPath;
        }
        catch
        {
            if (_incomingTransfers.TryRemove(chunk.TransferId, out var failed))
            {
                await failed.DisposeAsync(deleteTemporaryFile: true).ConfigureAwait(false);
            }

            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _outboundTransfers.Reader.ReadAllAsync(stoppingToken)
                           .ConfigureAwait(false))
        {
            try
            {
                await OfferAndSendAsync(request, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or CryptographicException
                    or InvalidDataException
                    or InvalidOperationException
                    or SocketException
                    or OperationCanceledException)
            {
                TransferFailed(logger, Guid.Empty.ToString(), request.FilePath, exception);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _outboundTransfers.Writer.TryComplete();

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (_, transfer) in _incomingTransfers)
        {
            await transfer.DisposeAsync(deleteTemporaryFile: true).ConfigureAwait(false);
        }

        _incomingTransfers.Clear();
    }

    private async Task OfferAndSendAsync(
        OutboundTransferRequest request,
        CancellationToken cancellationToken)
    {
        var transferId = Guid.NewGuid();
        var fileInfo = new FileInfo(request.FilePath);
        var sha256 = await ComputeHashAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
        var offer = new FileOfferPayload
        {
            TransferId = transferId,
            FileName = fileInfo.Name,
            Length = fileInfo.Length,
            Sha256 = sha256,
        };
        var pending = new PendingOutboundOffer(request.Recipient.Fingerprint);

        if (!_pendingOffers.TryAdd(transferId, pending))
        {
            throw new InvalidOperationException("Could not register the pending file offer.");
        }

        try
        {
            await node.SendEncryptedMessageAsync(
                    request.Recipient,
                    transferId,
                    ChatMessage.ContentTypes.FileOffer,
                    JsonSerializer.Serialize(offer),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!await pending.Response.Task
                    .WaitAsync(OfferResponseTimeout, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            await SendFileAsync(request, offer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pendingOffers.TryRemove(transferId, out _);
        }
    }

    private async Task SendFileAsync(
        OutboundTransferRequest request,
        FileOfferPayload offer,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            request.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = GC.AllocateUninitializedArray<byte>(ChunkSize);
        var sequence = 0;

        do
        {
            var bytesRead = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            var chunk = new FileChunkPayload
            {
                TransferId = offer.TransferId,
                Sequence = sequence++,
                IsFinal = file.Position == file.Length,
                Data = Convert.ToBase64String(buffer, 0, bytesRead),
            };

            await node.SendEncryptedMessageAsync(
                    request.Recipient,
                    offer.TransferId,
                    ChatMessage.ContentTypes.FileChunk,
                    JsonSerializer.Serialize(chunk),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        while (file.Position < file.Length);
    }

    private Task SendResponseAsync(
        Guid transferId,
        bool accepted,
        Peer sender,
        CancellationToken cancellationToken) =>
        node.SendEncryptedMessageAsync(
            sender,
            transferId,
            ChatMessage.ContentTypes.FileResponse,
            JsonSerializer.Serialize(
                new FileResponsePayload
                {
                    TransferId = transferId,
                    Accepted = accepted,
                }),
            cancellationToken);

    private static async Task<string> ComputeHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void ValidateOffer(FileOfferPayload offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.TransferId == Guid.Empty
            || offer.Length < 0
            || string.IsNullOrWhiteSpace(offer.FileName)
            || !string.Equals(offer.FileName, Path.GetFileName(offer.FileName), StringComparison.Ordinal)
            || offer.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The file offer contains invalid metadata.");
        }

        byte[] hash;
        try
        {
            hash = Convert.FromHexString(offer.Sha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The file offer hash is malformed.", exception);
        }

        if (hash.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidDataException("The file offer hash has an invalid length.");
        }
    }

    private sealed record OutboundTransferRequest(Peer Recipient, string FilePath);

    private sealed record PendingOutboundOffer(
        Fingerprint RecipientFingerprint,
        TaskCompletionSource<bool> Response)
    {
        public PendingOutboundOffer(Fingerprint recipientFingerprint)
            : this(
                recipientFingerprint,
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously))
        {
        }
    }

    private sealed class IncomingTransfer
    {
        private readonly FileOfferPayload _offer;
        private readonly string _temporaryPath;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly FileStream _stream;
        private long _bytesWritten;
        private int _nextSequence;
        private bool _completed;

        private IncomingTransfer(
            FileOfferPayload offer,
            Fingerprint senderFingerprint,
            string temporaryPath,
            FileStream stream)
        {
            _offer = offer;
            SenderFingerprint = senderFingerprint;
            _temporaryPath = temporaryPath;
            _stream = stream;
        }

        public Fingerprint SenderFingerprint { get; }

        public static IncomingTransfer Create(FileOfferPayload offer, Fingerprint senderFingerprint)
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "BackChannel");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{offer.TransferId:N}.part");
            var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new IncomingTransfer(offer, senderFingerprint, temporaryPath, stream);
        }

        public async Task<string?> WriteChunkAsync(
            FileChunkPayload chunk,
            CancellationToken cancellationToken)
        {
            if (_completed || chunk.Sequence != _nextSequence)
            {
                throw new InvalidDataException("The file chunks are out of order.");
            }

            byte[] data;
            try
            {
                data = Convert.FromBase64String(chunk.Data);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("The file chunk data is malformed.", exception);
            }

            if (data.LongLength > _offer.Length - _bytesWritten)
            {
                throw new InvalidDataException("The file data exceeds the offered file size.");
            }

            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            _hash.AppendData(data);
            _bytesWritten += data.Length;
            _nextSequence++;

            if (!chunk.IsFinal)
            {
                return null;
            }

            if (_bytesWritten != _offer.Length)
            {
                throw new InvalidDataException("The file data length does not match the accepted offer.");
            }

            var actualHash = Convert.ToHexString(_hash.GetHashAndReset());
            if (!string.Equals(actualHash, _offer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The received file hash does not match the accepted offer.");
            }

            _completed = true;
            await _stream.DisposeAsync().ConfigureAwait(false);
            _hash.Dispose();

            var destinationPath = GetDestinationPath(
                Path.GetDirectoryName(_temporaryPath)!,
                _offer.FileName);
            File.Move(_temporaryPath, destinationPath);
            return destinationPath;
        }

        public async ValueTask DisposeAsync(bool deleteTemporaryFile)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _hash.Dispose();

            if (deleteTemporaryFile && File.Exists(_temporaryPath))
            {
                File.Delete(_temporaryPath);
            }
        }

        private static string GetDestinationPath(string directory, string fileName)
        {
            var candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var suffix = 1; ; suffix++)
            {
                candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
