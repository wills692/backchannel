using System.Net;
using System.Threading.Channels;
using BackChannel.Core.Crypto;
using BackChannel.Core.Networking;
using BackChannel.Core.Peers;
using Xunit;

namespace BackChannel.Core.Tests.Networking;

public sealed class TwoNodeIntegrationTests
{
    [Fact]
    public async Task HelloAnnounceAndEncryptedChatCompleteAcrossLoopback()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;

        using var aliceIdentity = IdentityKeyPair.Create(2048);
        using var bobIdentity = IdentityKeyPair.Create(2048);
        var aliceEvents = Channel.CreateUnbounded<InboundEvent>();
        var bobEvents = Channel.CreateUnbounded<InboundEvent>();
        var alicePeers = new PeerRegistry();
        var bobPeers = new PeerRegistry();

        await using var aliceTcp = new TcpMessageListener(
            aliceEvents.Writer,
            LoopbackTcpOptions());
        await using var bobTcp = new TcpMessageListener(
            bobEvents.Writer,
            LoopbackTcpOptions());
        await aliceTcp.StartAsync(cancellationToken);
        await bobTcp.StartAsync(cancellationToken);

        await using var aliceDiscovery = new UdpDiscoveryService(
            new LocalNode(
                "ALICE-PC",
                "Alice",
                aliceIdentity.PublicIdentity,
                aliceTcp.LocalPort),
            alicePeers,
            aliceEvents.Writer,
            LoopbackUdpOptions());
        await using var bobDiscovery = new UdpDiscoveryService(
            new LocalNode(
                "BOB-PC",
                "Bob",
                bobIdentity.PublicIdentity,
                bobTcp.LocalPort),
            bobPeers,
            bobEvents.Writer,
            LoopbackUdpOptions());
        await aliceDiscovery.StartAsync(cancellationToken);
        await bobDiscovery.StartAsync(cancellationToken);

        await bobDiscovery.SendHelloAsync(
            new IPEndPoint(IPAddress.Loopback, aliceDiscovery.LocalPort),
            cancellationToken);

        var aliceSawBob = await ReadUntilAsync<PeerDiscoveredEvent>(
            aliceEvents.Reader,
            cancellationToken);
        var bobSawAlice = await ReadUntilAsync<PeerDiscoveredEvent>(
            bobEvents.Reader,
            cancellationToken);

        Assert.Equal(bobIdentity.Fingerprint, aliceSawBob.Peer.Fingerprint);
        Assert.Equal(aliceIdentity.Fingerprint, bobSawAlice.Peer.Fingerprint);
        Assert.Equal(bobTcp.LocalPort, aliceSawBob.Peer.TcpEndpoint.Port);
        Assert.Equal(aliceTcp.LocalPort, bobSawAlice.Peer.TcpEndpoint.Port);
        Assert.True(alicePeers.TryGet(bobIdentity.Fingerprint, out _));
        Assert.True(bobPeers.TryGet(aliceIdentity.Fingerprint, out var alicePeer));
        Assert.NotNull(alicePeer);

        bobDiscovery.UpdateDisplayName("Robert");
        await bobDiscovery.SendHelloAsync(
            new IPEndPoint(IPAddress.Loopback, aliceDiscovery.LocalPort),
            cancellationToken);

        var aliceSawRenamedBob = await ReadUntilAsync<PeerDiscoveredEvent>(
            aliceEvents.Reader,
            cancellationToken);

        Assert.Equal(PeerRegistrationChange.Updated, aliceSawRenamedBob.Change);
        Assert.Equal("Robert", aliceSawRenamedBob.Peer.DisplayName);

        var encryptedMessage = HybridCipher.Encrypt(
            "hello across loopback",
            Guid.NewGuid(),
            bobIdentity,
            [aliceIdentity.PublicIdentity]);
        await TcpMessageSender.SendAsync(
            alicePeer.TcpEndpoint,
            encryptedMessage,
            cancellationToken);

        var received = await ReadUntilAsync<ChatMessageReceivedEvent>(
            aliceEvents.Reader,
            cancellationToken);
        var plaintext = HybridCipher.Decrypt(
            received.Message,
            bobIdentity.PublicIdentity,
            aliceIdentity);

        Assert.Equal("hello across loopback", plaintext);
        Assert.Equal(IPAddress.Loopback, received.RemoteEndpoint.Address);

        await bobDiscovery.SendGoodbyeAsync(
            new IPEndPoint(IPAddress.Loopback, aliceDiscovery.LocalPort),
            cancellationToken);
        var departed = await ReadUntilAsync<PeerDepartedEvent>(
            aliceEvents.Reader,
            cancellationToken);

        Assert.Equal(bobIdentity.Fingerprint, departed.Fingerprint);
        Assert.False(alicePeers.TryGet(bobIdentity.Fingerprint, out _));
    }

    private static TcpListenerOptions LoopbackTcpOptions() =>
        new()
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            MaximumConcurrentConnections = 4,
        };

    private static UdpDiscoveryOptions LoopbackUdpOptions() =>
        new()
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            ReuseAddress = false,
        };

    private static async Task<TEvent> ReadUntilAsync<TEvent>(
        ChannelReader<InboundEvent> reader,
        CancellationToken cancellationToken)
        where TEvent : InboundEvent
    {
        while (true)
        {
            var inboundEvent = await reader.ReadAsync(cancellationToken);

            if (inboundEvent is NetworkFaultEvent fault)
            {
                throw new InvalidOperationException(
                    $"{fault.Component} reported a network fault.",
                    fault.Error);
            }

            if (inboundEvent is TEvent expected)
            {
                return expected;
            }
        }
    }
}
