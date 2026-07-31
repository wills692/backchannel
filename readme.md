# BackChannel

BackChannel is a terminal-based LAN messenger built as a networking and
cryptography exercise. Nodes discover one another with UDP and exchange signed,
end-to-end encrypted messages over length-prefixed TCP connections.

The application targets .NET 10. Identities, peers, and conversations are
intentionally ephemeral: restarting the process creates a new RSA identity and
clears all local state.

## Build and run

```powershell
dotnet build backchannel.slnx
dotnet test backchannel.slnx
dotnet run --project src/BackChannel.App/BackChannel.App.csproj
```

Start BackChannel on two or more machines on the same LAN. Each node
automatically announces itself every 30 seconds; `/hello` sends an immediate
announcement. The default discovery port is UDP 52520, while TCP uses an
ephemeral port advertised during discovery.

Firewalls must allow inbound UDP traffic on the configured discovery port and
inbound TCP traffic for the selected listener port. Setting a fixed `TcpPort`
can make that firewall rule easier to define.

## Commands

| Command | Purpose |
| --- | --- |
| `/hello` | Broadcast an immediate presence announcement. |
| `/peers` | Show discovered display names, endpoints, and key fingerprints. |
| `/msg` | Select and activate a one-to-one conversation. |
| `/msg <name> [message]` | Select a matching peer and optionally send immediately. |
| `/group [name]` | Create a group, or reactivate a received group with the same name. |
| `/help` | Show the command list. |
| `/quit` | Broadcast a goodbye message and stop cleanly. |

Once a conversation is active, any line without a leading slash is encrypted
and sent to that conversation.

Every group member must first be visible in every other member's `/peers` list.
The first group message carries the signed group name and complete participant
fingerprint list, allowing recipients to reconstruct the same conversation and
reply to everyone. A received conversation becomes active when no other
conversation is active; `/group <name>` can reactivate it later. Group
membership is fixed for that conversation. If a member
leaves or expires, the affected local conversation is removed rather than
silently weakening its recipient set.

## Configuration

Configuration is loaded from
`src/BackChannel.App/appsettings.json` when developing and from the copied
`appsettings.json` beside the executable at runtime. Standard .NET environment
variables and command-line arguments override JSON values.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `DisplayName` | current user | Human-readable peer name. |
| `MachineName` | current machine | Machine label advertised to peers. |
| `DiscoveryPort` | `52520` | UDP port used for LAN discovery. |
| `DiscoveryTarget` | empty | Optional IPv4 `address:port`; empty uses broadcast. |
| `TcpPort` | `0` | TCP listener port; zero asks the OS for an ephemeral port. |
| `MaximumConcurrentConnections` | `32` | Maximum simultaneous inbound TCP clients. |
| `AnnouncementIntervalSeconds` | `30` | Presence heartbeat interval. |
| `PeerTimeoutSeconds` | `90` | Remove peers not observed within this period. |

Examples:

```powershell
# Command-line override
dotnet run --project src/BackChannel.App -- --BackChannel:DisplayName=Alice

# PowerShell environment-variable override
$env:BackChannel__DisplayName = "Alice"
dotnet run --project src/BackChannel.App
```

`DiscoveryTarget` is mainly useful for deterministic localhost or routed
testing. For example, two local processes can use different UDP ports and point
at one another:

```powershell
dotnet run --project src/BackChannel.App -- --BackChannel:DisplayName=Alice --BackChannel:DiscoveryPort=52521 --BackChannel:DiscoveryTarget=127.0.0.1:52522
dotnet run --project src/BackChannel.App -- --BackChannel:DisplayName=Bob   --BackChannel:DiscoveryPort=52522 --BackChannel:DiscoveryTarget=127.0.0.1:52521
```

## Protocol overview

Discovery uses JSON envelopes over UDP:

1. `hello` advertises a display name, RSA public key, and TCP port.
2. A receiver registers the key fingerprint and replies with `announce`.
3. `goodbye` removes a matching ephemeral peer.
4. Periodic hello messages refresh `LastSeenUtc`; stale entries are removed.

Chat uses protocol version 2:

1. The sender generates a random 256-bit AES content key.
2. The plaintext is encrypted with AES-256-GCM.
3. The content key is wrapped separately for every recipient using
   RSA-OAEP-SHA256.
4. The sender signs the message metadata, group membership, wrapped keys,
   nonce, ciphertext, and authentication tag using RSA-PSS-SHA256.
5. TCP frames use a four-byte big-endian length prefix and a 1 MiB limit.

Core protocol, cryptography, registries, and transports live in
`BackChannel.Core`. Hosting, configuration, command dispatch, logging, and
Spectre.Console rendering live in `BackChannel.App`. Network listeners publish
events through a channel so the terminal shell remains the only component that
changes interactive UI state.

Daily diagnostic logs are written to `logs/backchannel-yyyyMMdd.log` beside the
application.

## Security boundaries

BackChannel demonstrates secure primitives but is not a production messenger:

- Private keys are memory-only and change on every restart.
- Peer keys use trust on first use and are not independently authenticated.
- There is no forward secrecy, key rotation, history, replay cache, or durable
  trust database.
- Conversation names, participant fingerprints, timing, and ciphertext sizes
  are authenticated but not hidden.
- LAN discovery is unauthenticated and can be spoofed or flooded.
- A compromised endpoint can read messages addressed to it and impersonate that
  endpoint for the lifetime of its private key.

Those constraints are deliberate so packet framing, discovery, hybrid
encryption, signatures, and lifecycle behavior remain visible and approachable.
