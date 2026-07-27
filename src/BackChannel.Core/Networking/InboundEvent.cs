using System.Net;
using BackChannel.Core.Crypto;
using BackChannel.Core.Peers;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Networking;

public abstract record InboundEvent(DateTimeOffset ReceivedAtUtc);

public sealed record PeerDiscoveredEvent(
    Peer Peer,
    PeerRegistrationChange Change,
    DateTimeOffset ReceivedAtUtc)
    : InboundEvent(ReceivedAtUtc);

public sealed record PeerDepartedEvent(
    Fingerprint Fingerprint,
    DateTimeOffset ReceivedAtUtc)
    : InboundEvent(ReceivedAtUtc);

public sealed record ChatMessageReceivedEvent(
    ChatMessage Message,
    IPEndPoint RemoteEndpoint,
    DateTimeOffset ReceivedAtUtc)
    : InboundEvent(ReceivedAtUtc);

public sealed record NetworkFaultEvent(
    string Component,
    Exception Error,
    IPEndPoint? RemoteEndpoint,
    DateTimeOffset ReceivedAtUtc)
    : InboundEvent(ReceivedAtUtc);
