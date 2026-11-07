using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Reports;

/// <summary>
/// The report sections "Play strength" and "Calibration", the same for every game that has an oracle. They read the ratings of the
/// moves from the protocol: a protocol without them, which an older one or a game without an oracle writes, gets neither section.
/// </summary>
internal static class PlayStrengthSection
{
    public static void Append(StringBuilder sb, MatchRecord match, ReportText t)
    {
        if (!match.RecordsOracle)
        {
            return;
        }

        var play = Seat.All.ToDictionary(seat => seat, seat => PlayMetrics.For(match, seat));
        AppendStrength(sb, play, t);
        if (Seat.All.Any(seat => match.Verdicts(seat).Any(v => v.Estimate is not null)))
        {
            AppendCalibration(sb, play, t);
        }
    }

    private static void AppendStrength(StringBuilder sb, Dictionary<Seat, PlayMetrics> play, ReportText t)
    {
        sb.AppendLine().AppendLine("## " + t["report.strength"]).AppendLine().AppendLine(t.Format("report.strength.note", ("rules", t["report.rules"]))).AppendLine().AppendLine(t["report.metrics.header"]);
        Row(sb, t["report.strength.moves_won"], play, p => p.Strength?.MovesInWonPosition.ToString(CultureInfo.InvariantCulture), t);
        Row(sb, t["report.strength.optimal"], play, p => p.Strength?.OptimalMoves.ToString(CultureInfo.InvariantCulture), t);
        Row(sb, t["report.strength.squandered"], play, p => p.Strength?.SquanderedWins.ToString(CultureInfo.InvariantCulture), t);
        Row(sb, t["report.strength.first_mistake"], play, p => FirstMistake(p.Strength, t), t);
        Row(sb, t["report.strength.rate"], play, p => Rate(p.Strength?.MistakeRate, t), t);
        sb.AppendLine();
    }

    private static void AppendCalibration(StringBuilder sb, Dictionary<Seat, PlayMetrics> play, ReportText t)
    {
        sb.AppendLine().AppendLine("## " + t["report.calibration"]).AppendLine().AppendLine(t["report.calibration.note"]).AppendLine().AppendLine(t["report.metrics.header"]);
        Row(sb, t["report.calibration.won"], play, p => Mean(p.Calibration?.EstimateInWonPositions, "0.#", t), t);
        Row(sb, t["report.calibration.lost"], play, p => Mean(p.Calibration?.EstimateInLostPositions, "0.#", t), t);
        Row(sb, t["report.calibration.brier"], play, p => Mean(p.Calibration?.Brier, "0.0000", t), t);
        sb.AppendLine();
    }

    private static string? FirstMistake(StrengthMetrics? strength, ReportText t) => strength switch
    {
        null or { MovesInWonPosition: 0 } => null,
        { FirstMistake: null } => t["report.strength.none"],
        { FirstMistake: { } first } => t.Format("report.strength.first_mistake_line", ("turn", first.TurnId), ("number", first.MoveNumber.ToString(CultureInfo.InvariantCulture))),
    };

    private static string? Rate(Fraction? rate, ReportText t) => rate is { } r
        ? t.Format(
            "report.strength.rate_value",
            ("count", r.Numerator.ToString("0", CultureInfo.InvariantCulture)),
            ("total", r.Denominator.ToString("0", CultureInfo.InvariantCulture)),
            ("percent", (r.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)))
        : null;

    private static string? Mean(Fraction? mean, string format, ReportText t) => mean is { } m
        ? t.Format("report.calibration.value", ("value", m.Value.ToString(format, CultureInfo.InvariantCulture)), ("n", m.Denominator.ToString("0", CultureInfo.InvariantCulture)))
        : null;

    private static void Row(StringBuilder sb, string label, Dictionary<Seat, PlayMetrics> play, Func<PlayMetrics, string?> value, ReportText t) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {Cell(value(play[Seat.A]), t)} | {Cell(value(play[Seat.B]), t)} |");

    private static string Cell(string? text, ReportText t) => MarkdownTable.Cell(text ?? t["report.na"]);
}
