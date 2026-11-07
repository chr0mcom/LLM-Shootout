using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmShootout.Arena.Protocol;

/// <summary>
/// Source-generated JSON for the protocol: camelCase property names, enums as text, no nulls, and readable umlauts
/// (the files are quoted in articles, so <c>ä</c> is written as <c>ä</c>, not as <c>\u00E4</c>). Dictionary keys such as
/// seats (<c>A</c>, <c>B</c>) and answer keys (<c>rules_followed</c>) are written unchanged.
/// </summary>
[JsonSerializable(typeof(MatchEvent))]
[JsonSerializable(typeof(JsonObject))]
public sealed partial class ProtocolJson : JsonSerializerContext
{
    private static readonly ProtocolJson Readable = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    });

    /// <summary>Serializes an event as one line.</summary>
    public static string ToLine(MatchEvent matchEvent) => JsonSerializer.Serialize(matchEvent, Readable.MatchEvent);

    /// <summary>Reads an event from one line.</summary>
    public static MatchEvent FromLine(string line) =>
        JsonSerializer.Deserialize(line, Readable.MatchEvent) ?? throw new JsonException("Empty protocol line.");
}
