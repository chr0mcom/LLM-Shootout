using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.ModelAccess;

namespace LlmShootout.Arena.Series;

/// <summary>Builds readable, unique directory names such as <c>2026-11-20_split-or-steal_gpt-4o_vs_claude_01</c>.</summary>
public static class MatchPaths
{
    /// <summary>The next free directory for a single match.</summary>
    public static string Match(string baseDirectory, DateTimeOffset date, string gameId, string playerA, string playerB) =>
        FirstFree(baseDirectory, $"{date:yyyy-MM-dd}_{gameId}_{Slug(playerA)}_vs_{Slug(playerB)}", i => string.Create(CultureInfo.InvariantCulture, $"_{i:00}"));

    /// <summary>
    /// The next free directory for a series. <paramref name="seriesName"/> is the base name of the series file, so that two series
    /// of the same game with the same players get different directories; a series without a file passes the id of the game.
    /// </summary>
    public static string Series(string baseDirectory, DateTimeOffset date, string seriesName, string playerA, string playerB) =>
        FirstFree(baseDirectory, $"{date:yyyy-MM-dd}_{Clean(seriesName, "series")}_{Slug(playerA)}_vs_{Slug(playerB)}_series", i => i == 1 ? string.Empty : $"-{i}");

    /// <summary>The directory of match <paramref name="number"/> (1-based) inside a series directory, for example <c>01-A-first</c>.</summary>
    public static string SeriesMatch(string seriesDirectory, int number, Seat firstSeat) =>
        Path.Combine(seriesDirectory, string.Create(CultureInfo.InvariantCulture, $"{number:00}-{firstSeat}-first"));

    /// <summary>Makes a player name usable in a file name: <c>openrouter:openai/gpt-4o</c> and <c>ollama:gpt-4o</c> become <c>gpt-4o</c>.</summary>
    public static string Slug(string player)
    {
        var (endpoint, model) = EndpointRegistry.Split(player);
        var name = endpoint is null or "scripted" ? player : model;
        return Clean(name[(name.LastIndexOf('/') + 1)..], "player");
    }

    private static string Clean(string name, string fallback)
    {
        var clean = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '-')]).Trim('-');
        return clean.Length == 0 ? fallback : clean;
    }

    /// <summary>The first name that is free in the folder; <paramref name="suffix"/> turns the attempt number into the ending (the first attempt may have none).</summary>
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
