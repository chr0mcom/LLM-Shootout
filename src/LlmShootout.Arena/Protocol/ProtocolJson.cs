using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmShootout.Arena.Protocol;

/// <summary>Source-generated JSON settings for the protocol: camelCase, enums as text, no nulls.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchEvent))]
[JsonSerializable(typeof(JsonObject))]
public sealed partial class ProtocolJson : JsonSerializerContext
{
    /// <summary>Serializes an event as one line.</summary>
    public static string ToLine(MatchEvent matchEvent) => JsonSerializer.Serialize(matchEvent, Default.MatchEvent);

    /// <summary>Reads an event from one line.</summary>
    public static MatchEvent FromLine(string line) =>
        JsonSerializer.Deserialize(line, Default.MatchEvent) ?? throw new JsonException("Empty protocol line.");
}
