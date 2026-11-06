using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Prompting;

/// <summary>Builds the JSON schema of the answer of a turn, for models that support structured output.</summary>
public static class ResponseSchema
{
    /// <summary>Schema for <c>{thought, message, move}</c>. All keys are present; unused ones are null.</summary>
    public static JsonNode ForTurn(TurnRequest turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("thought", "message", "move"),
            ["properties"] = new JsonObject
            {
                ["thought"] = NullableString(),
                ["message"] = NullableString(),
                ["move"] = MoveSchema(turn),
            },
        };
    }

    private static JsonObject NullableString() => new() { ["type"] = new JsonArray("string", "null") };

    private static JsonNode MoveSchema(TurnRequest turn)
    {
        if (turn.Format?.SchemaFragment is { } fragment)
        {
            return fragment.DeepClone();
        }

        if (turn.Format?.AllowedValues is { Count: > 0 } allowed)
        {
            var values = new JsonArray();
            foreach (var value in allowed)
            {
                values.Add(value);
            }

            values.Add(null);
            return new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = values };
        }

        return NullableString();
    }
}
