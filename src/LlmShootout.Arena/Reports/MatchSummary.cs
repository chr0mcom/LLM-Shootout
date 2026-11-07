using System.Text.Json;
using System.Text.Json.Serialization;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Reports;

/// <summary>
/// How a seat played in the positions it could win (<see cref="PlayMetrics.Strength"/>). A rate or a turn that does not exist is written as
/// <c>null</c>: "not applicable", never 0.
/// </summary>
public sealed record StrengthSummary(
    int MovesInWonPosition,
    int OptimalMoves,
    int SquanderedWins,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FirstMistakeTurn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? FirstMistakeMove,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? MistakeRate);

/// <summary>
/// How the estimates of a seat fit its positions (<see cref="PlayMetrics.Calibration"/>). Each mean comes with the number of
/// estimates behind it; a mean without any is written as <c>null</c>: "not applicable", never 0.
/// </summary>
public sealed record CalibrationSummary(
    int EstimatesInWonPositions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? MeanEstimateInWonPositions,
    int EstimatesInLostPositions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? MeanEstimateInLostPositions,
    int BrierCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Brier);

/// <summary>
/// Numbers of one seat in one match. <c>InvalidAnswers</c>, <c>Refusals</c> and <c>TokenLimitHits</c> count rejected answers,
/// <c>RuleViolations</c> accepted moves that the rules forbid, <c>Resignations</c> given-up games, and <c>ExhaustedTurns</c> turns
/// without an acceptable move after all attempts; the last three are separate from each other and from the rejected answers.
/// <c>Strength</c> and <c>Calibration</c> are <c>null</c> if the protocol has no ratings by an oracle (the game has none, or the
/// protocol is older): not applicable. <c>ViolationInLostPositionThenWon</c> is true if the seat broke a rule in a lost position
/// and won, and <c>null</c> if it broke rules but the position is not recorded.
/// </summary>
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
    int RuleViolations,
    int Resignations,
    int ExhaustedTurns,
    int ThoughtsTruncated,
    int MessagesTruncated,
    int SilentMessages,
    int IdentityHits,
    int PromptTokens,
    int CompletionTokens,
    int ReasoningTokens,
    decimal CostUsd,
    bool? DebriefValid,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] StrengthSummary? Strength,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CalibrationSummary? Calibration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? ViolationInLostPositionThenWon);

/// <summary>
/// The content of <c>match.json</c>: everything the report shows, as numbers. <c>ResultWithRuleViolation</c> is true if the game
/// ended and a seat made an accepted but forbidden move before the end. It appears here and in the report only, never in a prompt or a debriefing.
/// </summary>
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
    bool ResultWithRuleViolation,
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
