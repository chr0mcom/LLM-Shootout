using System.Text.Json;
using System.Text.Json.Serialization;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Reports;

/// <summary>Numbers of one seat in one match.</summary>
public sealed record SeatSummary(
    string Player,
    string? RequestedModel,
    string? DeliveredModel,
    double? Score,
    string? Label,
    int Turns,
    int InvalidAnswers,
    int Refusals,
    int TokenLimitHits,
    int ThoughtsTruncated,
    int MessagesTruncated,
    int SilentMessages,
    int IdentityHits,
    int PromptTokens,
    int CompletionTokens,
    int ReasoningTokens,
    decimal CostUsd,
    bool? DebriefValid);

/// <summary>The content of <c>match.json</c>: everything the report shows, as numbers.</summary>
public sealed record MatchSummary(
    string GameId,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    bool Aborted,
    string? AbortReason,
    bool Communication,
    string Language,
    long Seed,
    Seat FirstSeat,
    string? Outcome,
    IReadOnlyDictionary<Seat, SeatSummary> Seats,
    decimal CostUsd,
    int CostEstimatedCalls,
    int CostUnknownCalls,
    int ModelCalls,
    string AppVersion,
    string GitCommit,
    IReadOnlyDictionary<string, string> TemplateHashes);

/// <summary>The content of <c>series.json</c>.</summary>
public sealed record SeriesSummary(
    string GameId,
    string PlayerA,
    string PlayerB,
    int Matches,
    int Completed,
    int Aborted,
    decimal CostUsd,
    int CostEstimatedCalls,
    int CostUnknownCalls,
    IReadOnlyList<SeriesEntry> Entries,
    IReadOnlyList<SeriesPlayer> Players);

/// <summary>One match of a series.</summary>
public sealed record SeriesEntry(string Directory, Seat FirstSeat, string? Outcome, decimal CostUsd);

/// <summary>The metrics the game declares for one player, summed over all matches and both seats.</summary>
public sealed record SeriesPlayer(string Player, IReadOnlyList<SeriesMetricValue> Metrics);

/// <summary>
/// One metric of one player. <c>Value</c> is always written; it is null, shown as "n/a", if nothing could be measured
/// (for a ratio: the denominator is 0).
/// </summary>
public sealed record SeriesMetricValue(
    string Key,
    string Label,
    MetricKind Kind,
    double Numerator,
    double? Denominator,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Value);

/// <summary>Source-generated JSON for summaries: camelCase, indented, enums as text, readable umlauts, dictionary keys unchanged.</summary>
[JsonSerializable(typeof(MatchSummary))]
[JsonSerializable(typeof(SeriesSummary))]
public sealed partial class SummaryJson : JsonSerializerContext
{
    private static readonly SummaryJson Readable = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    });

    /// <summary>Serializes a match summary.</summary>
    public static string Write(MatchSummary summary) => JsonSerializer.Serialize(summary, Readable.MatchSummary);

    /// <summary>Serializes a series summary.</summary>
    public static string Write(SeriesSummary summary) => JsonSerializer.Serialize(summary, Readable.SeriesSummary);
}
