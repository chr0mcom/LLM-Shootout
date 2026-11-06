// Adapted from AiBehavior.PublicGoods (MIT, same author).
using System.Text.Json;

namespace LlmShootout.Arena.Models;

/// <summary>
/// Recovers a JSON answer that a reasoning model put into the OpenRouter <c>reasoning</c> channel instead of
/// <c>message.content</c>. Some model and provider combinations return empty content and place the complete
/// answer object at the end of the reasoning trace.
/// </summary>
public static class ReasoningJsonRecovery
{
    /// <summary>
    /// Extracts the last balanced, parseable top-level JSON object from a reasoning string.
    /// Braces inside string literals are ignored, so prose cannot produce false matches.
    /// </summary>
    public static bool TryExtractJsonObject(string? reasoning, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(reasoning))
        {
            return false;
        }

        var candidates = FindTopLevelObjects(reasoning);
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
                case '"':
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
