using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>Report sections of Split or Steal: the table of the matches of a series.</summary>
public sealed class SplitOrStealAnalyzer(ITemplateSource templates) : IGameAnalyzer
{
    /// <inheritdoc />
    public IReadOnlyList<ReportSection> AnalyzeMatch(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return [];
    }

    /// <inheritdoc />
    public IReadOnlyList<ReportSection> AnalyzeSeries(IReadOnlyList<MatchRecord> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count == 0)
        {
            return [];
        }

        var language = matches[0].Started.Language;
        var t = new ReportText(templates, language);
        var body = new StringBuilder().AppendLine(t["report.series.matches"]);
        var index = 0;
        foreach (var m in matches)
        {
            index++;
            body.AppendLine(CultureInfo.InvariantCulture, $"| {index} | {Comm(m, language)} | {m.Started.FirstSeat} | {Decision(m, Seat.A)} | {Decision(m, Seat.B)} | {Points(m, Seat.A)} | {Points(m, Seat.B)} | {m.Result?.Outcome ?? "aborted"} |");
        }

        body.AppendLine().AppendLine("### " + t["report.series.comparison.title"]).AppendLine().AppendLine(t["report.series.comparison.header"]);
        foreach (var group in new[] { true, false })
        {
            var subset = matches.Where(m => m.Communication == group && m.Result is not null).ToArray();
            if (subset.Length > 0)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"| {t[group ? "report.series.comm.on" : "report.series.comm.off"]} | {subset.Length} | {StealRate(subset):P0} | {subset.Count(m => m.Result!.Outcome == "both_split")} | {subset.Average(m => m.Result!.Seats[Seat.A].Score):0.#} | {subset.Average(m => m.Result!.Seats[Seat.B].Score):0.#} |");
            }
        }

        return [new ReportSection(t["report.series.title"], body.ToString().TrimEnd())];
    }

    private static double StealRate(MatchRecord[] matches)
    {
        var decisions = matches.SelectMany(m => Seat.All.Select(s => DecisionOf(m, s))).Where(d => d is SplitOrStealGame.Split or SplitOrStealGame.Steal).ToArray();
        return decisions.Length == 0 ? 0 : decisions.Count(d => d == SplitOrStealGame.Steal) / (double)decisions.Length;
    }

    private string Comm(MatchRecord match, string language) =>
        templates.Render(new TemplateRef(match.Communication ? "report.series.comm.on" : "report.series.comm.off"), language);

    private static string? DecisionOf(MatchRecord match, Seat seat) =>
        match.FactData(seat, SplitOrStealGame.DecisionFact, SplitOrStealGame.DecisionFact);

    private static string Decision(MatchRecord match, Seat seat) => DecisionOf(match, seat) ?? "-";

    private static string Points(MatchRecord match, Seat seat) =>
        match.Result is { } r ? r.Seats[seat].Score.ToString("0.##", CultureInfo.InvariantCulture) : "-";
}
