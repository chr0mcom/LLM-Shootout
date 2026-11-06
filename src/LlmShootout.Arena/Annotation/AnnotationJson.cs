using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmShootout.Arena.Annotation;

/// <summary>Source-generated JSON for annotation files: camelCase, indented, readable umlauts.</summary>
[JsonSerializable(typeof(AnnotationFile))]
[JsonSerializable(typeof(JsonObject))]
public sealed partial class AnnotationJson : JsonSerializerContext
{
    private static readonly AnnotationJson Readable = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    });

    /// <summary>Serializes an annotation file.</summary>
    public static string Write(AnnotationFile file) => JsonSerializer.Serialize(file, Readable.AnnotationFile);

    /// <summary>Reads an annotation file.</summary>
    public static AnnotationFile? Read(string json) => JsonSerializer.Deserialize(json, Readable.AnnotationFile);
}
