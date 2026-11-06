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
    bool IdentityRevealed,
    string? Outcome,
    IReadOnlyDictionary<Seat, SeatSummary> Seats,
    decimal CostUsd,
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
    int CostUnknownCalls,
    IReadOnlyDictionary<Seat, double> PointsTotal,
    IReadOnlyList<SeriesEntry> Entries);

/// <summary>One match of a series.</summary>
public sealed record SeriesEntry(string Directory, bool Communication, string? Outcome, decimal CostUsd);

/// <summary>Source-generated JSON settings for summaries.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MatchSummary))]
[JsonSerializable(typeof(SeriesSummary))]
public sealed partial class SummaryJson : JsonSerializerContext
{
    /// <summary>Serializes a match summary.</summary>
    public static string Write(MatchSummary summary) => JsonSerializer.Serialize(summary, Default.MatchSummary);

    /// <summary>Serializes a series summary.</summary>
    public static string Write(SeriesSummary summary) => JsonSerializer.Serialize(summary, Default.SeriesSummary);
}
