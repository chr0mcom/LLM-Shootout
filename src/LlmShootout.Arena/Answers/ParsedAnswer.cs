using System.Text.Json;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Models;

namespace LlmShootout.Arena.Answers;

/// <summary>The three fields of a model answer, as far as they could be read.</summary>
/// <param name="HasJson">True if a JSON object was found.</param>
/// <param name="Thought">Private thought, or null.</param>
/// <param name="Message">Public message, or null.</param>
/// <param name="Move">Move as text, or null.</param>
/// <param name="ExtraKeys">Keys of the object that are not part of the contract.</param>
/// <param name="FromReasoning">True if the object had to be recovered from the reasoning channel.</param>
public sealed record ParsedAnswer(
    bool HasJson,
    string? Thought,
    string? Message,
    string? Move,
    IReadOnlyList<string> ExtraKeys,
    bool FromReasoning)
{
    /// <summary>An answer without any usable JSON.</summary>
    public static ParsedAnswer None { get; } = new(false, null, null, null, [], false);
}

/// <summary>Reads the JSON object out of the raw text of an answer.</summary>
public static class AnswerParser
{
    private static readonly string[] KnownKeys = ["thought", "message", "move"];

    /// <summary>
    /// Finds the answer object in the content (plain, in a code fence, or surrounded by text) and, if the content
    /// holds none, in the reasoning channel.
    /// </summary>
    public static ParsedAnswer Parse(string? content, string? reasoning)
    {
        if (TryReadObject(content, out var fromContent))
        {
            return fromContent with { FromReasoning = false };
        }

        return TryReadObject(reasoning, out var fromReasoning) ? fromReasoning with { FromReasoning = true } : ParsedAnswer.None;
    }

    private static bool TryReadObject(string? text, out ParsedAnswer answer)
    {
        answer = ParsedAnswer.None;
        if (!ReasoningJsonRecovery.TryExtractJsonObject(text, out var json))
        {
            return false;
        }

        var node = JsonNode.Parse(json)!.AsObject();
        var extra = node.Select(p => p.Key).Where(k => !KnownKeys.Contains(k, StringComparer.Ordinal)).ToArray();
        answer = new ParsedAnswer(true, ReadText(node, "thought"), ReadText(node, "message"), ReadText(node, "move"), extra, false);
        return true;
    }

    private static string? ReadText(JsonObject node, string key)
    {
        if (!node.TryGetPropertyValue(key, out var value) || value is null)
        {
            return null;
        }

        var text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
