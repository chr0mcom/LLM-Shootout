using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Reports;

/// <summary>
/// The report section "Rule compliance", the same for every game: rejected answers, rule violations, resignations and
/// exhausted turns per player, the answer to <c>rules_followed</c> next to the counted behavior, and the mark of a result
/// that was reached with a rule violation. If the game has an oracle, it also names the position before each violation and
/// whether a seat broke a rule in a lost position and then won. It shows only what the protocol contains: an older protocol has
/// no kinds of move.
/// </summary>
internal static class RuleComplianceSection
{
    private const string RulesFollowed = "rules_followed";

    public static void Append(StringBuilder sb, MatchRecord match, ReportText t)
    {
        var play = Seat.All.ToDictionary(seat => seat, seat => PlayMetrics.For(match, seat));
        var recorded = PlayMetrics.RecordsMoveKinds(match);
        sb.AppendLine().AppendLine("## " + t["report.rules"]).AppendLine().AppendLine(t["report.rules.note"]).AppendLine().AppendLine(t["report.metrics.header"]);
        Row(sb, t["report.rules.rejected_invalid"], play, p => p.InvalidAnswers);
        Row(sb, t["report.rules.rejected_refusal"], play, p => p.Refusals);
        Row(sb, t["report.rules.rejected_length"], play, p => p.TokenLimitHits);
        if (recorded)
        {
            Row(sb, t["report.rules.violations"], play, p => p.RuleViolations);
            Row(sb, t["report.rules.resignations"], play, p => p.Resignations);
        }

        Row(sb, t["report.rules.exhausted"], play, p => p.ExhaustedTurns);
        if (recorded && match.RecordsOracle)
        {
            TextRow(sb, t["report.rules.lost_then_won"], play, p => Answer(p.ViolationInLostPositionThenWon, t));
        }

        AppendDebriefAnswer(sb, match, t);
        sb.AppendLine();
        AppendViolations(sb, play, recorded, t);
        if (PlayMetrics.ResultWithRuleViolation(match))
        {
            sb.AppendLine(t["report.rules.result_flag"]).AppendLine();
        }
    }

    private static void AppendDebriefAnswer(StringBuilder sb, MatchRecord match, ReportText t)
    {
        var answers = Seat.All.ToDictionary(seat => seat, seat => match.Debriefs.LastOrDefault(d => d.Seat == seat)?.Answers.GetValueOrDefault(RulesFollowed));
        if (answers.Values.All(a => a is null))
        {
            return;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.rules.debrief_answer"]} | {MarkdownTable.Cell(Shown(answers[Seat.A], t))} | {MarkdownTable.Cell(Shown(answers[Seat.B], t))} |");
    }

    private static string Shown(string? answer, ReportText t) => answer is null ? "-" : t.Answer(answer);

    private static void AppendViolations(StringBuilder sb, Dictionary<Seat, PlayMetrics> play, bool recorded, ReportText t)
    {
        var violations = Seat.All.SelectMany(seat => play[seat].Violations.Select(v => (Seat: seat, Violation: v))).ToArray();
        if (violations.Length == 0)
        {
            if (recorded)
            {
                sb.AppendLine(t["report.rules.none"]).AppendLine();
            }

            return;
        }

        sb.AppendLine(t["report.rules.violation_list"]);
        foreach (var (seat, v) in violations)
        {
            sb.AppendLine(t.Format("report.rules.violation_line", ("speaker", t.Format("who.seat", ("seat", seat.ToString()))), ("turn", v.TurnId), ("move", v.Move), ("code", v.Code), ("position", Position(v, t))));
        }

        sb.AppendLine();
    }

    private static string Position(RuleViolationRecord violation, ReportText t) =>
        violation.Position is { } position ? t.Format("report.rules.violation_position", ("position", t[$"report.position.{OracleFact.Name(position)}"])) : string.Empty;

    private static string Answer(bool? value, ReportText t) => value switch
    {
        true => t["report.yes"],
        false => t["report.no"],
        null => t["report.na"],
    };

    private static void Row(StringBuilder sb, string label, Dictionary<Seat, PlayMetrics> play, Func<PlayMetrics, int> value) =>
        TextRow(sb, label, play, p => value(p).ToString(CultureInfo.InvariantCulture));

    private static void TextRow(StringBuilder sb, string label, Dictionary<Seat, PlayMetrics> play, Func<PlayMetrics, string> value) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {value(play[Seat.A])} | {value(play[Seat.B])} |");
}
