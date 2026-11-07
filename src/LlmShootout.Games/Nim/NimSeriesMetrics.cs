using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;

namespace LlmShootout.Games.Nim;

/// <summary>
/// The numbers that the series report of Nim summarizes per player, over both seats: wins, mistake rate, given-up won positions, rule
/// violations, resignations, forfeits and the Brier score of the estimates. A ratio is the sum of its counts divided by the sum of its
/// bases over all matches, so a player that never had a base gets no value (n/a), not 0. No metric combines what a player said in the
/// debriefing with what it did.
/// </summary>
internal static class NimSeriesMetrics
{
    /// <summary>All metrics in the order of the table.</summary>
    public static IReadOnlyList<SeriesMetric> All { get; } =
    [
        Metric("wins", "series.wins", MetricKind.Sum, Wins),
        Metric("mistake_rate", "series.mistake_rate", MetricKind.Ratio, (match, seat) => PlayMetrics.For(match, seat).Strength?.MistakeRate),
        Metric("squandered", "series.squandered", MetricKind.Sum, (match, seat) => Count(PlayMetrics.For(match, seat).Strength?.SquanderedWins)),
        Metric("rule_violations", "series.violations", MetricKind.Sum, (match, seat) => CountIfKindsRecorded(match, PlayMetrics.For(match, seat).RuleViolations)),
        Metric("resignations", "series.resignations", MetricKind.Sum, (match, seat) => CountIfKindsRecorded(match, PlayMetrics.For(match, seat).Resignations)),
        Metric("forfeits", "series.forfeits", MetricKind.Sum, Forfeits),
        Metric("brier", "series.brier", MetricKind.Mean, (match, seat) => PlayMetrics.For(match, seat).Calibration?.Brier),
    ];

    private static SeriesMetric Metric(string key, string label, MetricKind kind, Func<MatchRecord, Seat, Fraction?> measure) =>
        new(key, TemplateRef.Of(label), kind, measure);

    private static Fraction? Wins(MatchRecord match, Seat seat) =>
        match.Result is { } result ? new Fraction(result.Seats[seat].Score > result.Seats[seat.Opponent].Score ? 1 : 0) : null;

    private static Fraction? Forfeits(MatchRecord match, Seat seat) =>
        match.Result is { } result ? new Fraction(result.Seats[seat].Label == NimGame.Labels.Forfeit ? 1 : 0) : null;

    private static Fraction? Count(int? count) => count is { } n ? new Fraction(n) : null;

    private static Fraction? CountIfKindsRecorded(MatchRecord match, int count) =>
        PlayMetrics.RecordsMoveKinds(match) ? new Fraction(count) : null;
}
