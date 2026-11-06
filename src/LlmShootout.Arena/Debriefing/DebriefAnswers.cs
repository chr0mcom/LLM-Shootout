using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Debriefing;

/// <summary>Builds the schema and sample answers of a questionnaire and validates answers against the question types.</summary>
public static class DebriefAnswers
{
    /// <summary>JSON schema of the answer: one required key per question, plus <c>_reason</c> keys.</summary>
    public static JsonNode Schema(IReadOnlyList<DebriefingQuestion> questions)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var q in questions)
        {
            properties[q.Key] = TypeSchema(q);
            required.Add(q.Key);
            if (q.WithReason)
            {
                properties[q.Key + "_reason"] = new JsonObject { ["type"] = "string" };
                required.Add(q.Key + "_reason");
            }
        }

        return new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
    }

    /// <summary>A valid answer for every question, drawn with the given generator. For random and scripted players.</summary>
    public static string Sample(IReadOnlyList<DebriefingQuestion> questions, SeededRng rng, string sampleText)
    {
        var answer = new JsonObject();
        foreach (var q in questions)
        {
            answer[q.Key] = q.Kind switch
            {
                QuestionKind.Scale => q.Min + rng.NextInt(q.Max - q.Min + 1),
                QuestionKind.Boolean => rng.NextBool(0.5),
                QuestionKind.Choice => q.Choices![rng.NextInt(q.Choices.Count)],
                _ => sampleText,
            };
            if (q.WithReason)
            {
                answer[q.Key + "_reason"] = sampleText;
            }
        }

        return answer.ToJsonString();
    }

    /// <summary>
    /// Checks an answer object. Returns the answers as text (<c>true</c>, <c>3</c>, free text) and the violations
    /// as (key, kind of violation) pairs.
    /// </summary>
    public static (Dictionary<string, string> Answers, List<(string Key, QuestionKind Kind, string Problem)> Violations) Validate(
        IReadOnlyList<DebriefingQuestion> questions,
        JsonObject answer)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        var violations = new List<(string, QuestionKind, string)>();
        foreach (var q in questions)
        {
            if (!TryRead(q, answer[q.Key], out var value))
            {
                violations.Add((q.Key, q.Kind, answer[q.Key] is null ? "missing" : "invalid"));
                continue;
            }

            answers[q.Key] = value;
            if (q.WithReason)
            {
                var reason = answer[q.Key + "_reason"]?.ToString();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    violations.Add((q.Key + "_reason", QuestionKind.Text, "missing"));
                }
                else
                {
                    answers[q.Key + "_reason"] = reason.Trim();
                }
            }
        }

        return (answers, violations);
    }

    private static bool TryRead(DebriefingQuestion q, JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node is null)
        {
            return false;
        }

        var kind = node.GetValueKind();
        var text = kind == JsonValueKind.String ? node.GetValue<string>().Trim() : node.ToJsonString();
        switch (q.Kind)
        {
            case QuestionKind.Scale when kind == JsonValueKind.Number && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= q.Min && number <= q.Max:
                value = number.ToString(CultureInfo.InvariantCulture);
                return true;
            case QuestionKind.Boolean when kind is JsonValueKind.True or JsonValueKind.False:
                value = kind == JsonValueKind.True ? "true" : "false";
                return true;
            case QuestionKind.Choice when kind == JsonValueKind.String && q.Choices?.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase)) is { } choice:
                value = choice;
                return true;
            case QuestionKind.Text when text.Length > 0 && kind == JsonValueKind.String:
                value = text;
                return true;
            default:
                return false;
        }
    }

    private static JsonObject TypeSchema(DebriefingQuestion q) => q.Kind switch
    {
        QuestionKind.Scale => new JsonObject { ["type"] = "integer", ["minimum"] = q.Min, ["maximum"] = q.Max },
        QuestionKind.Boolean => new JsonObject { ["type"] = "boolean" },
        QuestionKind.Choice => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. q.Choices!.Select(c => (JsonNode?)c)]) },
        _ => new JsonObject { ["type"] = "string" },
    };
}
