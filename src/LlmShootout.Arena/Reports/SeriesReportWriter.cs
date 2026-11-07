using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Arena.Reports;

/// <summary>Writes <c>series.json</c> and <c>series-report.md</c>.</summary>
public static class SeriesReportWriter
{
    /// <summary>Name of the series summary file.</summary>
    public const string SummaryFile = "series.json";

    /// <summary>Name of the series report file.</summary>
    public const string ReportFile = "series-report.md";

    /// <summary>Collects the numbers of a series from its matches.</summary>
    public static SeriesSummary Summarize(
        IGameDefinition definition,
        string playerA,
        string playerB,
        IReadOnlyList<MatchRecord> matches,
        string language)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(matches);
        return new SeriesSummary(
            definition.Id,
            playerA,
            playerB,
            matches.Count,
            matches.Count(r => r.Result is not null),
            matches.Count(r => r.Aborted is not null),
            matches.Sum(r => r.CostUsd),
            matches.Sum(r => r.CostEstimatedCalls),
            matches.Sum(r => r.CostUnknownCalls),
            [.. matches.Select(r => new SeriesEntry(Path.GetFileName(r.Directory), r.Started.FirstSeat, r.Result?.Outcome, r.CostUsd))],
            PlayerMetrics(definition, matches, language));
    }

    /// <summary>Writes both files into the series directory.</summary>
    public static async Task WriteAsync(IGameDefinition definition, string directory, SeriesSummary summary, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(summary);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, SummaryFile), SummaryJson.Write(summary), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, ReportFile), BuildReport(definition, summary, language), cancellationToken);
    }

    /// <summary>Builds the Markdown report of a series: overview, list of matches, and one table per player for the metrics the game declares.</summary>
    public static string BuildReport(IGameDefinition definition, SeriesSummary summary, string language)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(summary);
        var t = new ReportText(ArenaTemplates.Default, language);
        var sb = new StringBuilder();
        sb.AppendLine(t.Format("report.series.title", ("game", definition.DisplayName), ("a", summary.PlayerA), ("b", summary.PlayerB))).AppendLine();
        AppendOverview(sb, t, definition, summary);
        AppendMatches(sb, t, new ReportText(definition.Templates, language), summary);
        foreach (var player in summary.Players)
        {
            AppendPlayer(sb, t, player);
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendOverview(StringBuilder sb, ReportText t, IGameDefinition definition, SeriesSummary summary)
    {
        sb.AppendLine("## " + t["report.series.overview"]).AppendLine().AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.series.stat.game"]} | {MarkdownTable.Cell(definition.DisplayName)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.series.stat.matches"]} | {summary.Completed} / {summary.Aborted} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.series.stat.cost"]} | {summary.CostUsd:0.0000} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.series.stat.unknown"]} | {summary.CostUnknownCalls} |").AppendLine();
    }

    private static void AppendMatches(StringBuilder sb, ReportText t, ReportText game, SeriesSummary summary)
    {
        sb.AppendLine("## " + t["report.series.list"]).AppendLine().AppendLine(t["report.series.list.header"]);
        for (var i = 0; i < summary.Entries.Count; i++)
        {
            var e = summary.Entries[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {i + 1} | [{e.Directory}]({e.Directory}/report.md) | {e.FirstSeat} | {(e.Outcome is { } outcome ? game.Word("result.outcome", outcome) : "-")} | {e.CostUsd:0.0000} |");
        }
    }

    private static void AppendPlayer(StringBuilder sb, ReportText t, SeriesPlayer player)
    {
        sb.AppendLine().AppendLine("## " + t.Format("report.series.player", ("player", player.Player))).AppendLine().AppendLine(t["report.series.metrics.header"]);
        foreach (var metric in player.Metrics)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {MarkdownTable.Cell(metric.Label)} | {Value(t, metric)} | {Basis(metric)} |");
        }
    }

    private static string Value(ReportText t, SeriesMetricValue metric) => metric switch
    {
        { Value: null } => t["report.series.na"],
        { Kind: MetricKind.Ratio } => metric.Value.Value.ToString("P1", CultureInfo.InvariantCulture),
        { Kind: MetricKind.Mean } => metric.Value.Value.ToString("0.0000", CultureInfo.InvariantCulture),
        _ => metric.Value.Value.ToString("0.##", CultureInfo.InvariantCulture),
    };

    private static string Basis(SeriesMetricValue metric) => metric.Denominator is { } denominator
        ? string.Create(CultureInfo.InvariantCulture, $"{metric.Numerator:0.##} / {denominator:0.##}")
        : "-";

    /// <summary>One entry per player name that took part in the series, with every metric the game declares. Empty if the game declares none.</summary>
    private static List<SeriesPlayer> PlayerMetrics(IGameDefinition definition, IReadOnlyList<MatchRecord> matches, string language)
    {
        var metrics = definition.Analyzer?.SeriesMetrics ?? [];
        var names = metrics.Count == 0 ? [] : matches.SelectMany(m => Seat.All.Select(seat => m.Started.Players[seat].Name)).Distinct(StringComparer.Ordinal);
        return [.. names.Select(name => new SeriesPlayer(name, [.. metrics.Select(metric => Combine(definition, metric, name, matches, language))]))];
    }

    private static SeriesMetricValue Combine(IGameDefinition definition, SeriesMetric metric, string player, IReadOnlyList<MatchRecord> matches, string language)
    {
        var parts = matches
            .SelectMany(m => Seat.All.Where(seat => m.Started.Players[seat].Name == player).Select(seat => metric.Measure(m, seat)))
            .OfType<Fraction>()
            .ToArray();
        var numerator = parts.Sum(p => p.Numerator);
        var denominator = parts.Sum(p => p.Denominator);
        var (shownDenominator, value) = metric.Kind switch
        {
            MetricKind.Ratio or MetricKind.Mean => ((double?)denominator, denominator == 0 ? null : (double?)(numerator / denominator)),
            _ => (null, parts.Length == 0 ? null : (double?)numerator),
        };
        return new SeriesMetricValue(metric.Key, definition.Templates.Render(metric.Label, language), metric.Kind, numerator, shownDenominator, value);
    }
}
