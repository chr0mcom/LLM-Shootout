// Adapted from AiBehavior.PublicGoods (MIT, same author), originally ReasoningJsonRecovery.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmShootout.Arena.Answers;

/// <summary>
/// Finds the JSON object of an answer in free text: plain, in a code fence, or surrounded by prose. Some models also
/// return empty content and put the complete answer object at the end of their reasoning trace, so the reasoning
/// channel is searched as well when the content holds none.
/// </summary>
public static class JsonObjectExtractor
{
    /// <summary>The first answer object found: in the content, otherwise in the reasoning channel. Null if neither holds one.</summary>
    public static (JsonObject Answer, bool FromReasoning)? FindAnswer(string? content, string? reasoning)
    {
        if (TryExtract(content, out var inContent))
        {
            return (JsonNode.Parse(inContent)!.AsObject(), false);
        }

        return TryExtract(reasoning, out var inReasoning) ? (JsonNode.Parse(inReasoning)!.AsObject(), true) : null;
    }

    /// <summary>
    /// Extracts the last balanced, parseable top-level JSON object from a text.
    /// Braces inside string literals are ignored, so prose cannot produce false matches.
    /// </summary>
    public static bool TryExtract(string? text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidates = FindTopLevelObjects(text);
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (IsJsonObject(candidates[i]))
            {
                json = candidates[i];
                return true;
            }
        }

        return false;
    }

    private static List<string> FindTopLevelObjects(string text)
    {
        var candidates = new List<string>();
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"' when depth > 0:
                    inString = true;
                    break;
                case '{':
                    if (depth++ == 0)
                    {
                        start = i;
                    }

                    break;
                case '}' when depth > 0:
                    if (--depth == 0)
                    {
                        candidates.Add(text.Substring(start, i - start + 1));
                    }

                    break;
            }
        }

        return candidates;
    }

    private static bool IsJsonObject(string candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
