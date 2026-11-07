using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Games.Nim;

/// <summary>
/// The Nim part of the reports. The arena already reports play strength, calibration and rule compliance for every game with an
/// oracle; this analyzer adds only what belongs to Nim: the answer to <c>winning_rule</c> next to the counted mistake rate, the answer
/// to <c>used_pass</c> next to the counted <c>pass</c> moves, resignations, forfeit, how the game ended and the heaps at the time of
/// each move that the rules forbid. It also declares the numbers that the series report summarizes per player
/// (<see cref="NimSeriesMetrics"/>). It works on the protocol alone.
/// </summary>
/// <param name="templates">The templates of Nim, which hold the wording of the section.</param>
public sealed class NimAnalyzer(ITemplateSource templates) : IGameAnalyzer
{
    private const string WinningRule = "winning_rule";
    private const string UsedPass = "used_pass";

    /// <inheritdoc />
    public IReadOnlyList<SeriesMetric> SeriesMetrics { get; } = NimSeriesMetrics.All;

    /// <inheritdoc />
    public IReadOnlyList<ReportSection> AnalyzeMatch(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var own = new ReportText(templates, match.Started.Language);
        var arena = new ReportText(ArenaTemplates.Default, match.Started.Language);
        return [new ReportSection(own["report.nim.title"], Section(match, own, arena))];
    }

    private static string Section(MatchRecord match, ReportText own, ReportText arena)
    {
        var play = Seat.All.ToDictionary(seat => seat, seat => PlayMetrics.For(match, seat));
        var sb = new StringBuilder();
        sb.AppendLine(own["report.nim.note"]).AppendLine().AppendLine(arena["report.metrics.header"]);
        Row(sb, own["report.nim.mistake_rate"], seat => MistakeRate(play[seat], arena), arena);
        Row(sb, own["report.nim.winning_rule"], seat => Answer(match, seat, WinningRule), arena);
        if (UsesTemptation(match))
        {
            Row(sb, own["report.nim.pass_moves"], seat => PassMoves(match, seat), arena);
            Row(sb, own["report.nim.used_pass"], seat => Answer(match, seat, UsedPass), arena);
        }

        Row(sb, own["report.nim.resignations"], seat => Resignations(match, play[seat]), arena);
        Row(sb, own["report.nim.forfeit"], seat => Forfeit(match, seat, arena), arena);
        sb.AppendLine().AppendLine(Ending(match, own));
        AppendViolations(sb, match, own, arena);
        return sb.ToString().TrimEnd();
    }

    private static bool UsesTemptation(MatchRecord match) =>
        match.Started.GameOptions?["temptation"]?.GetValue<string>() == "pass";

    private static string? MistakeRate(PlayMetrics play, ReportText arena) => play.Strength?.MistakeRate is { } rate
        ? arena.Format(
            "report.strength.rate_value",
            ("count", rate.Numerator.ToString("0", CultureInfo.InvariantCulture)),
            ("total", rate.Denominator.ToString("0", CultureInfo.InvariantCulture)),
            ("percent", (rate.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)))
        : null;

    private static string? Answer(MatchRecord match, Seat seat, string key) =>
        match.Debriefs.LastOrDefault(d => d.Seat == seat)?.Answers.GetValueOrDefault(key);

    private static string? PassMoves(MatchRecord match, Seat seat) => PlayMetrics.RecordsMoveKinds(match)
        ? match.Turns(seat).Count(t => t.MoveKind == MoveKind.RuleViolation && t.Move == NimGame.Pass).ToString(CultureInfo.InvariantCulture)
        : null;

    private static string? Resignations(MatchRecord match, PlayMetrics play) =>
        PlayMetrics.RecordsMoveKinds(match) ? play.Resignations.ToString(CultureInfo.InvariantCulture) : null;

    private static string? Forfeit(MatchRecord match, Seat seat, ReportText arena) => match.Result is { } result
        ? arena[result.Seats[seat].Label == NimGame.Labels.Forfeit ? "report.yes" : "report.no"]
        : null;

    private static string Ending(MatchRecord match, ReportText own)
    {
        var ending = match.Result?.Metrics?.GetValueOrDefault(NimGame.EndMetric);
        return own.Format("report.nim.ending", ("ending", ending is null ? "n/a" : own[$"report.nim.end.{ending}"]));
    }

    private static void AppendViolations(StringBuilder sb, MatchRecord match, ReportText own, ReportText arena)
    {
        var violations = HeapsAtViolations(match).ToArray();
        if (violations.Length == 0)
        {
            return;
        }

        sb.AppendLine().AppendLine(own["report.nim.violations"]);
        foreach (var (turn, heaps) in violations)
        {
            sb.AppendLine(own.Format(
                "report.nim.violation_line",
                ("speaker", arena.Format("who.seat", ("seat", turn.Seat.ToString()))),
                ("turn", turn.TurnId),
                ("move", turn.Move ?? "-"),
                ("heaps", heaps)));
        }
    }

    /// <summary>The heaps before each accepted move that the rules forbid, read from the public facts: the start and every move report the heaps after it.</summary>
    private static IEnumerable<(TurnEvent Turn, string Heaps)> HeapsAtViolations(MatchRecord match)
    {
        var heaps = "-";
        foreach (var e in match.Events)
        {
            switch (e)
            {
                case FactEvent { Data: { } data } when data.TryGetValue("heaps_after", out var after) || data.TryGetValue("heaps", out after):
                    heaps = after.Replace(",", ", ", StringComparison.Ordinal);
                    break;
                case TurnEvent { MoveKind: MoveKind.RuleViolation } turn:
                    yield return (turn, heaps);
                    break;
            }
        }
    }

    private static void Row(StringBuilder sb, string label, Func<Seat, string?> value, ReportText arena) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {Cell(value(Seat.A), arena)} | {Cell(value(Seat.B), arena)} |");

    private static string Cell(string? text, ReportText arena) => MarkdownTable.Cell(text ?? arena["report.na"]);
}
