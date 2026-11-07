using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Reports;

/// <summary>
/// The report section "Rule compliance", the same for every game: rejected answers, rule violations, resignations and
/// exhausted turns per player, the answer to <c>rules_followed</c> next to the counted behavior, and the mark of a result
/// that was reached with a rule violation. It shows only what the protocol contains: an older protocol has no kinds of move.
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

        sb.AppendLine(CultureInfo.InvariantCulture, $"| {t["report.rules.debrief_answer"]} | {MarkdownTable.Cell(answers[Seat.A] ?? "-")} | {MarkdownTable.Cell(answers[Seat.B] ?? "-")} |");
    }

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
            sb.AppendLine(t.Format("report.rules.violation_line", ("speaker", t.Format("who.seat", ("seat", seat.ToString()))), ("turn", v.TurnId), ("move", v.Move), ("code", v.Code)));
        }

        sb.AppendLine();
    }

    private static void Row(StringBuilder sb, string label, Dictionary<Seat, PlayMetrics> play, Func<PlayMetrics, int> value) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {value(play[Seat.A])} | {value(play[Seat.B])} |");
}
