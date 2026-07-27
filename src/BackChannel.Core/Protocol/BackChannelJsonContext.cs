using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackChannel.Core.Protocol;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(AnnounceMessage))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(GoodbyeMessage))]
[JsonSerializable(typeof(RecipientKeyEnvelope))]
public sealed partial class BackChannelJsonContext : JsonSerializerContext;
