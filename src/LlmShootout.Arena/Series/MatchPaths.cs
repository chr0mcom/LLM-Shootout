using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.ModelAccess;

namespace LlmShootout.Arena.Series;

/// <summary>Builds readable, unique directory names such as <c>2026-11-20_split-or-steal_gpt-4o_vs_claude_01</c>.</summary>
public static class MatchPaths
{
    /// <summary>The next free directory for a single match.</summary>
    public static string Match(string baseDirectory, DateTimeOffset date, string gameId, string playerA, string playerB) =>
        Unique(baseDirectory, $"{date:yyyy-MM-dd}_{gameId}_{Slug(playerA)}_vs_{Slug(playerB)}");

    /// <summary>The next free directory for a series.</summary>
    public static string Series(string baseDirectory, DateTimeOffset date, string gameId, string playerA, string playerB) =>
        Unique(baseDirectory, $"{date:yyyy-MM-dd}_{gameId}_{Slug(playerA)}_vs_{Slug(playerB)}_series");

    /// <summary>The directory of match <paramref name="number"/> (1-based) inside a series directory.</summary>
    public static string SeriesMatch(string seriesDirectory, int number, bool communication, Seat firstSeat) =>
        Path.Combine(seriesDirectory, string.Create(CultureInfo.InvariantCulture, $"{number:00}-{(communication ? "talk" : "silent")}-{firstSeat}-first"));

    /// <summary>Makes a player name usable in a file name: <c>openrouter:openai/gpt-4o</c> and <c>claude:gpt-4o</c> become <c>gpt-4o</c>.</summary>
    public static string Slug(string player)
    {
        var (endpoint, model) = EndpointRegistry.Split(player);
        var name = endpoint is null or "scripted" ? player : model;
        name = name[(name.LastIndexOf('/') + 1)..];
        var slug = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '-')]).Trim('-');
        return slug.Length == 0 ? "player" : slug;
    }

    private static string Unique(string baseDirectory, string name)
    {
        if (name.EndsWith("_series", StringComparison.Ordinal))
        {
            return FirstFree(baseDirectory, name, suffix: i => i == 1 ? string.Empty : $"-{i}");
        }

        return FirstFree(baseDirectory, name, suffix: i => string.Create(CultureInfo.InvariantCulture, $"_{i:00}"));
    }

    private static string FirstFree(string baseDirectory, string name, Func<int, string> suffix)
    {
        for (var i = 1; ; i++)
        {
            var path = Path.Combine(baseDirectory, name + suffix(i));
            if (!Directory.Exists(path))
            {
                return path;
            }
        }
    }
}
