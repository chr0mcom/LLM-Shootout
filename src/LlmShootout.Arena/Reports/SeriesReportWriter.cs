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
    public static SeriesSummary Summarize(string gameId, string playerA, string playerB, IReadOnlyList<MatchRecord> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return new SeriesSummary(
            gameId,
            playerA,
            playerB,
            matches.Count,
            matches.Count(r => r.Result is not null),
            matches.Count(r => r.Aborted is not null),
            matches.Sum(r => r.CostUsd),
            matches.Sum(r => r.CostUnknownCalls),
            Seat.All.ToDictionary(seat => seat, seat => matches.Sum(r => r.Result?.Seats[seat].Score ?? 0)),
            [.. matches.Select(r => new SeriesEntry(Path.GetFileName(r.Directory), r.Communication, r.Result?.Outcome, r.CostUsd))]);
    }

    /// <summary>Writes both files into the series directory.</summary>
    public static async Task WriteAsync(
        IGameDefinition definition,
        string directory,
        SeriesSummary summary,
        IReadOnlyList<MatchRecord> matches,
        string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(matches);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, SummaryFile), SummaryJson.Write(summary), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, ReportFile), BuildReport(definition, summary, matches, language), cancellationToken);
    }

    /// <summary>Builds the Markdown report of a series.</summary>
    public static string BuildReport(IGameDefinition definition, SeriesSummary summary, IReadOnlyList<MatchRecord> matches, string language)
    {
        string T(string key, params (string, string)[] values) => ArenaTemplates.Default.Render(TemplateRef.Of(key, values), language);
        var sb = new StringBuilder();
        sb.AppendLine(T("report.series.title", ("game", definition.DisplayName), ("a", summary.PlayerA), ("b", summary.PlayerB))).AppendLine();
        sb.AppendLine("## " + T("report.series.overview")).AppendLine().AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.series.stat.matches")} | {summary.Completed} / {summary.Aborted} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.series.stat.cost")} | {summary.CostUsd:0.0000} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.series.stat.unknown")} | {summary.CostUnknownCalls} |").AppendLine();

        sb.AppendLine("## " + T("report.series.seats")).AppendLine().AppendLine(T("report.series.seats.header"));
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.series.metric.points")} | {summary.PointsTotal[Seat.A]:0.##} | {summary.PointsTotal[Seat.B]:0.##} |");
        var played = Math.Max(summary.Completed, 1);
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.series.metric.average")} | {summary.PointsTotal[Seat.A] / played:0.##} | {summary.PointsTotal[Seat.B] / played:0.##} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.metric.invalid")} | {matches.Sum(m => m.InvalidAnswers(Seat.A))} | {matches.Sum(m => m.InvalidAnswers(Seat.B))} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {T("report.metric.refusals")} | {matches.Sum(m => m.Refusals(Seat.A))} | {matches.Sum(m => m.Refusals(Seat.B))} |").AppendLine();

        sb.AppendLine("## " + T("report.series.list")).AppendLine().AppendLine(T("report.series.list.header"));
        for (var i = 0; i < summary.Entries.Count; i++)
        {
            var e = summary.Entries[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {i + 1} | [{e.Directory}]({e.Directory}/report.md) | {T(e.Communication ? "report.communication.on" : "report.communication.off")} | {e.Outcome ?? "-"} | {e.CostUsd:0.0000} |");
        }

        foreach (var section in definition.Analyzer?.AnalyzeSeries(matches) ?? [])
        {
            sb.AppendLine().AppendLine("## " + section.Title).AppendLine().AppendLine(section.Markdown);
        }

        return sb.ToString().TrimEnd() + "\n";
    }
}
