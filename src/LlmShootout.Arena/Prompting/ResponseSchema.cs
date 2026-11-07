using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Prompting;

/// <summary>Builds the JSON schema of the answer of a turn, for models that support structured output.</summary>
public static class ResponseSchema
{
    /// <summary>
    /// Schema for <c>{thought, message, move, estimate}</c>. All keys are present; unused ones are null. Without
    /// messages between players the <c>message</c> key does not exist at all, and without an estimate in the turn
    /// neither does the <c>estimate</c> key.
    /// </summary>
    public static JsonNode ForTurn(TurnRequest turn, bool communication = true)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var properties = new JsonObject { ["thought"] = NullableString() };
        var required = new JsonArray("thought");
        if (communication)
        {
            properties["message"] = NullableString();
            required.Add("message");
        }

        properties["move"] = MoveSchema(turn);
        required.Add("move");
        if (turn.Estimate != FieldRule.Forbidden)
        {
            properties["estimate"] = EstimateSchema(turn.Estimate);
            required.Add("estimate");
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties,
        };
    }

    private static JsonObject EstimateSchema(FieldRule rule) => new()
    {
        ["type"] = rule == FieldRule.Required ? "integer" : new JsonArray("integer", "null"),
        ["minimum"] = 0,
        ["maximum"] = 100,
    };

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
