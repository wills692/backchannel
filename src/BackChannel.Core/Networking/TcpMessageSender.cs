using System.Net;
using System.Net.Sockets;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Networking;

public static class TcpMessageSender
{
    public static async Task SendAsync(
        IPEndPoint endpoint,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(message);

        using var client = new TcpClient(endpoint.AddressFamily)
        {
            NoDelay = true,
        };

        await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await TcpFrameCodec.WriteAsync(stream, message, cancellationToken)
            .ConfigureAwait(false);
    }
}
