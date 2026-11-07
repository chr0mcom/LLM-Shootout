using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Games;

/// <summary>How a metric is combined over the matches of a series.</summary>
public enum MetricKind
{
    /// <summary>The numerators of all matches are added.</summary>
    Sum,

    /// <summary>The sum of all numerators is divided by the sum of all denominators.</summary>
    Ratio,
}

/// <summary>A count and the number it is measured against. For a <see cref="MetricKind.Sum"/> only the numerator counts.</summary>
/// <param name="Numerator">What happened.</param>
/// <param name="Denominator">What could have happened.</param>
public readonly record struct Fraction(double Numerator, double Denominator = 1)
{
    /// <summary>The numerator divided by the denominator. Only meaningful for a denominator above 0; a value without a basis is represented by a missing fraction.</summary>
    public double Value => Numerator / Denominator;
}

/// <summary>
/// A number that a game declares for the summary of a series. The series report summarizes exactly these metrics
/// for every player, over both seats, and nothing else.
/// </summary>
/// <param name="Key">Stable key for <c>series.json</c>.</param>
/// <param name="Label">Name of the metric as a template of the game, rendered in the language of the series.</param>
/// <param name="Kind">How the matches are combined.</param>
/// <param name="Measure">The contribution of one seat in one match, or null if the match has nothing to measure for it.</param>
public sealed record SeriesMetric(string Key, TemplateRef Label, MetricKind Kind, Func<MatchRecord, Seat, Fraction?> Measure);
