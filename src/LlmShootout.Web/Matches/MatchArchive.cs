using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LlmShootout.Arena.Protocol;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Matches;

/// <summary>One line of the history.</summary>
internal sealed record MatchListItem(
    string Name,
    DateTimeOffset At,
    string? GameId,
    string? PlayerA,
    string? PlayerB,
    string? Outcome,
    double? ScoreA,
    double? ScoreB,
    decimal? CostUsd,
    bool Aborted,
    bool Complete);

/// <summary>Reads the match directories below the matches folder. Names are checked, so no path leaves that folder.</summary>
internal sealed partial class MatchArchive(IOptions<WebOptions> web)
{
    private const int MaxItems = 200;

    public IReadOnlyList<MatchListItem> List()
    {
        var root = Path.GetFullPath(web.Value.MatchesDirectory);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, ProtocolFiles.Events)))
                .Select(d => (Directory: d, Written: File.GetLastWriteTimeUtc(Path.Combine(d, ProtocolFiles.Events))))
                .OrderByDescending(x => x.Written)
                .Take(MaxItems)
                .Select(x => ToItem(x.Directory, x.Written)),
        ];
    }

    /// <summary>The full path of a match directory, or null if the name is not a plain directory name or does not exist.</summary>
    public string? Resolve(string name)
    {
        if (!ValidName().IsMatch(name))
        {
            return null;
        }

        var root = Path.GetFullPath(web.Value.MatchesDirectory);
        var path = Path.GetFullPath(Path.Combine(root, name));
        return Path.GetDirectoryName(path) == root && Directory.Exists(path) ? path : null;
    }

    public FileStream? OpenFile(string name, string file)
    {
        var directory = Resolve(name);
        var path = directory is null ? null : Path.Combine(directory, file);
        return path is not null && File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) : null;
    }

    private static MatchListItem ToItem(string directory, DateTime written)
    {
        var name = Path.GetFileName(directory);
        var summary = ReadSummary(Path.Combine(directory, ProtocolFiles.Summary));
        if (summary is null)
        {
            return new MatchListItem(name, new DateTimeOffset(written, TimeSpan.Zero), null, null, null, null, null, null, null, false, false);
        }

        var seats = summary["seats"];
        return new MatchListItem(
            name,
            summary["startedAt"]?.GetValue<DateTimeOffset>() ?? new DateTimeOffset(written, TimeSpan.Zero),
            summary["gameId"]?.ToString(),
            seats?["A"]?["player"]?.ToString(),
            seats?["B"]?["player"]?.ToString(),
            summary["outcome"]?.ToString(),
            seats?["A"]?["score"]?.GetValue<double>(),
            seats?["B"]?["score"]?.GetValue<double>(),
            summary["costUsd"]?.GetValue<decimal>(),
            summary["aborted"]?.GetValue<bool>() ?? false,
            true);
    }

    private static JsonObject? ReadSummary(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$")]
    private static partial Regex ValidName();
}
