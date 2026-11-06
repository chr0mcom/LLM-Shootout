using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmShootout.Arena.Annotation;

/// <summary>Source-generated JSON settings for annotation files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotationFile))]
[JsonSerializable(typeof(JsonObject))]
public sealed partial class AnnotationJson : JsonSerializerContext
{
}
