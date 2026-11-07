using System.Text.Json;
using LlmShootout.Arena.Annotation;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Protocol;

/// <summary>A match as read back from its directory: the protocol events plus any derived files.</summary>
public sealed class MatchRecord
{
    private MatchRecord(string directory, IReadOnlyList<MatchEvent> events, MatchStartedEvent started, IReadOnlyList<AnnotationFile> annotations)
    {
        Directory = directory;
        Events = events;
        Started = started;
        Annotations = annotations;
    }

    /// <summary>The match directory.</summary>
    public string Directory { get; }

    /// <summary>All events in order.</summary>
    public IReadOnlyList<MatchEvent> Events { get; }

    /// <summary>The first event.</summary>
    public MatchStartedEvent Started { get; }

    /// <summary>Derived judge assessments found next to the protocol.</summary>
    public IReadOnlyList<AnnotationFile> Annotations { get; }

    /// <summary>Id of the game.</summary>
    public string GameId => Started.GameId;

    /// <summary>True if the players could talk.</summary>
    public bool Communication => Started.Communication;

    /// <summary>The game result, or null if the match was aborted.</summary>
    public GameResult? Result => Events.OfType<MatchFinishedEvent>().LastOrDefault()?.Result;

    /// <summary>The abort event, or null if the match ended normally.</summary>
    public MatchAbortedEvent? Aborted => Events.OfType<MatchAbortedEvent>().LastOrDefault();

    /// <summary>All public messages, optionally of one seat.</summary>
    public IEnumerable<MessageEvent> Messages(Seat? seat = null) => Events.OfType<MessageEvent>().Where(m => seat is null || m.Seat == seat);

    /// <summary>All finished turns, optionally of one seat.</summary>
    public IEnumerable<TurnEvent> Turns(Seat? seat = null) => Events.OfType<TurnEvent>().Where(t => seat is null || t.Seat == seat);

    /// <summary>All facts.</summary>
    public IEnumerable<FactEvent> Facts => Events.OfType<FactEvent>();

    /// <summary>All model calls, including those of the debriefing.</summary>
    public IEnumerable<ModelCallEvent> Calls => Events.OfType<ModelCallEvent>();

    /// <summary>The debriefing events of the match itself.</summary>
    public IEnumerable<DebriefEvent> Debriefs => Events.OfType<DebriefEvent>();

    /// <summary>Total cost: reported cost plus, where OpenRouter reported none, the estimate from tokens and prices.</summary>
    public decimal CostUsd => Calls.Sum(c => c.Call?.CostUsd ?? c.Call?.EstimatedCostUsd ?? 0m);

    /// <summary>Number of calls whose cost was estimated because OpenRouter reported none.</summary>
    public int CostEstimatedCalls => Calls.Count(c => c.Call is { CostUsd: null, EstimatedCostUsd: not null });

    /// <summary>Number of calls whose cost is unknown (neither reported nor estimable).</summary>
    public int CostUnknownCalls => Calls.Count(c => c.CostUnknown);

    /// <summary>Time between the first and the last event.</summary>
    public TimeSpan Duration => Events[^1].At - Events[0].At;

    /// <summary>Invalid answers of a seat.</summary>
    public int InvalidAnswers(Seat seat) => Turns(seat).Sum(t => t.InvalidAnswers);

    /// <summary>Answers of a seat that were cut off by the token limit (<c>finish_reason: length</c>).</summary>
    public int TokenLimitHits(Seat seat) => Turns(seat).Sum(t => t.TokenLimitHits);

    /// <summary>Refusals of a seat.</summary>
    public int Refusals(Seat seat) => Turns(seat).Sum(t => t.Refusals);

    /// <summary>The decision of a seat from the <c>decision</c> fact, or null.</summary>
    public string? FactData(Seat seat, string kind, string key) =>
        Facts.FirstOrDefault(f => f.Kind == kind && f.Seat == seat)?.Data?.GetValueOrDefault(key);

    /// <summary>Reads a match directory. Throws if there is no protocol or it does not start with <c>match_started</c>.</summary>
    public static MatchRecord Load(string directory)
    {
        if (!File.Exists(Path.Combine(directory, ProtocolFiles.Events)))
        {
            throw new FileNotFoundException($"'{directory}' is not a match directory: {ProtocolFiles.Events} is missing.");
        }

        var events = ProtocolReader.ReadAll(directory);
        var started = (events.Count > 0 ? events[0] : null) as MatchStartedEvent
            ?? throw new InvalidDataException($"'{directory}': the protocol does not start with a match_started event.");
        return new MatchRecord(directory, events, started, ReadAnnotations(directory));
    }

    private static List<AnnotationFile> ReadAnnotations(string directory) =>
    [
        .. System.IO.Directory.EnumerateFiles(directory, "annotation.*.json").Order(StringComparer.Ordinal)
            .Select(path => AnnotationJson.Read(File.ReadAllText(path)))
            .OfType<AnnotationFile>(),
    ];
}
