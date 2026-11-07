using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;

namespace LlmShootout.Arena.Protocol;

/// <summary>Version of the line format of <c>events.jsonl</c>. Increase it when the shape of an event changes.</summary>
public static class ProtocolVersion
{
    /// <summary>The current version.</summary>
    public const int Current = 1;
}

/// <summary>Base of all protocol events. One event is one line of <c>events.jsonl</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MatchStartedEvent), "match_started")]
[JsonDerivedType(typeof(ModelCallEvent), "model_call")]
[JsonDerivedType(typeof(MessageEvent), "message")]
[JsonDerivedType(typeof(TurnEvent), "turn")]
[JsonDerivedType(typeof(FactEvent), "fact")]
[JsonDerivedType(typeof(MatchFinishedEvent), "match_finished")]
[JsonDerivedType(typeof(MatchAbortedEvent), "match_aborted")]
[JsonDerivedType(typeof(DebriefEvent), "debrief")]
public abstract record MatchEvent(DateTimeOffset At)
{
    /// <summary>Version of the line format.</summary>
    public int SchemaVersion { get; init; } = ProtocolVersion.Current;
}

/// <summary>Who plays a seat.</summary>
public sealed record PlayerDescription(string Name, string? RequestedModel, IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>First event of a match: everything needed to understand and repeat it.</summary>
public sealed record MatchStartedEvent(
    DateTimeOffset At,
    string GameId,
    long Seed,
    string Language,
    bool Communication,
    Seat FirstSeat,
    JsonObject? GameOptions,
    IReadOnlyDictionary<Seat, PlayerDescription> Players,
    IReadOnlyDictionary<string, string> TemplateHashes,
    MatchOptions Options,
    string AppVersion,
    string GitCommit) : MatchEvent(At);

/// <summary>The fields of an answer as parsed by the arena.</summary>
public sealed record ParsedFields(string? Thought, string? Message, string? Move, bool FromReasoning, IReadOnlyList<string> ExtraKeys);

/// <summary>One call to a player, with the exact prompt and the unchanged answer.</summary>
public sealed record ModelCallEvent(
    DateTimeOffset At,
    string TurnId,
    CallPurpose Purpose,
    Seat Seat,
    int Attempt,
    IReadOnlyList<PromptMessage> Messages,
    string? RawContent,
    string? RawReasoning,
    ParsedFields? Parsed,
    string Class,
    IReadOnlyList<string> Violations,
    ModelCallInfo? Call,
    bool CostUnknown,
    string? Error,
    string? RawExchange) : MatchEvent(At)
{
    /// <summary>The event of a call that failed technically: no answer, the error text and the raw exchange.</summary>
    public static ModelCallEvent Failed(DateTimeOffset at, string turnId, CallPurpose purpose, Seat seat, int attempt, IReadOnlyList<PromptMessage> messages, PlayerFailureException failure) =>
        new(at, turnId, purpose, seat, attempt, messages, null, null, null, CallClass.TechnicalError, [], failure.Call, failure.Call?.CostIsUnknown() ?? true, failure.Message, failure.RawExchange);

    /// <summary>
    /// The event of a call that returned an answer. A message of the endpoint about the answer (for example a gateway that
    /// could not produce structured output) is added to the problems as <c>endpoint: …</c>.
    /// </summary>
    public static ModelCallEvent Answered(DateTimeOffset at, string turnId, CallPurpose purpose, Seat seat, int attempt, IReadOnlyList<PromptMessage> messages, PlayerReply reply, ParsedFields? parsed, string callClass, IReadOnlyList<string> problems) =>
        new(at, turnId, purpose, seat, attempt, messages, reply.Content, reply.Reasoning, parsed, callClass, reply.Notice is null ? problems : [.. problems, $"endpoint: {reply.Notice}"], reply.Call, reply.Call?.CostIsUnknown() ?? false, null, reply.RawExchange);
}

/// <summary>A public message became visible. A null text is silence.</summary>
public sealed record MessageEvent(
    DateTimeOffset At,
    string TurnId,
    Seat Seat,
    string? Text,
    bool Silent,
    bool Truncated,
    IReadOnlyList<string> IdentityHits) : MatchEvent(At);

/// <summary>The final outcome of one turn of one seat.</summary>
public sealed record TurnEvent(
    DateTimeOffset At,
    string TurnId,
    string Kind,
    Seat Seat,
    string? Move,
    string Status,
    string? Thought,
    bool ThoughtTruncated,
    int Attempts,
    int InvalidAnswers,
    int Refusals,
    int TokenLimitHits = 0) : MatchEvent(At);

/// <summary>A fact reported by the game.</summary>
public sealed record FactEvent(
    DateTimeOffset At,
    string TurnId,
    string Kind,
    FactAudience Audience,
    Seat? Seat,
    string Text,
    TemplateRef Template,
    IReadOnlyDictionary<string, string>? Data) : MatchEvent(At);

/// <summary>The game ended.</summary>
public sealed record MatchFinishedEvent(DateTimeOffset At, GameResult Result, decimal CostUsd, int CostUnknownCalls) : MatchEvent(At);

/// <summary>The match stopped before the game ended. Everything before this event is still valid.</summary>
public sealed record MatchAbortedEvent(DateTimeOffset At, string Reason, string Detail) : MatchEvent(At);

/// <summary>The debriefing answers of one seat.</summary>
public sealed record DebriefEvent(
    DateTimeOffset At,
    Seat Seat,
    bool Valid,
    IReadOnlyDictionary<string, string> Answers,
    IReadOnlyList<string> Problems,
    int Attempts) : MatchEvent(At);
