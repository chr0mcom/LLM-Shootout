using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Reports;

/// <summary>One accepted move that the rules forbid: where it happened, what it was, and which rule it broke.</summary>
/// <param name="TurnId">The turn of the move, for example <c>move-7</c>.</param>
/// <param name="Move">The move as the game normalized it.</param>
/// <param name="Code">The name of the broken rule, chosen by the game. <c>-</c> if the protocol has no fact for it.</param>
public sealed record RuleViolationRecord(string TurnId, string Move, string Code);

/// <summary>
/// The general numbers of one seat in one match that every game has: rejected answers, rule violations, resignations and
/// exhausted turns. The match report, <c>match.json</c> and the series metrics of a game all read them from here, so
/// they cannot disagree. Everything is counted from the protocol; the arena assumes no intent.
/// </summary>
/// <param name="InvalidAnswers">Answers that were not usable and not a refusal and not cut off by the length limit.</param>
/// <param name="Refusals">Answers that declined to play.</param>
/// <param name="TokenLimitHits">Answers that were cut off by the output limit.</param>
/// <param name="Violations">The accepted moves that the rules forbid, in order.</param>
/// <param name="Resignations">Turns in which the seat gave the game up.</param>
/// <param name="ExhaustedTurns">Turns in which the seat used up all attempts without an acceptable move. This is not a rule violation.</param>
public sealed record PlayMetrics(
    int InvalidAnswers,
    int Refusals,
    int TokenLimitHits,
    IReadOnlyList<RuleViolationRecord> Violations,
    int Resignations,
    int ExhaustedTurns)
{
    /// <summary>All answers that were rejected and had to be repeated.</summary>
    public int RejectedAnswers => InvalidAnswers + Refusals + TokenLimitHits;

    /// <summary>The number of accepted moves that the rules forbid.</summary>
    public int RuleViolations => Violations.Count;

    /// <summary>Reads the numbers of one seat from the protocol.</summary>
    public static PlayMetrics For(MatchRecord match, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(match);
        var turns = match.Turns(seat).ToArray();
        return new PlayMetrics(
            match.InvalidAnswers(seat),
            match.Refusals(seat),
            match.TokenLimitHits(seat),
            [.. turns.Where(t => t.MoveKind == MoveKind.RuleViolation).Select(t => Violation(match, seat, t))],
            turns.Count(t => t.MoveKind == MoveKind.Resignation),
            turns.Count(t => t.Status == TurnStatus.Exhausted));
    }

    /// <summary>True if the game ended and either seat made an accepted move that the rules forbid before the end.</summary>
    public static bool ResultWithRuleViolation(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Result is not null && match.Turns().Any(t => t.MoveKind == MoveKind.RuleViolation);
    }

    /// <summary>True if the protocol can tell a rule violation or a resignation from a regular move (protocol version 2 and later).</summary>
    public static bool RecordsMoveKinds(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Started.SchemaVersion >= 2;
    }

    private static RuleViolationRecord Violation(MatchRecord match, Seat seat, TurnEvent turn)
    {
        var fact = match.Facts.FirstOrDefault(f => f.Kind == ArenaFacts.RuleViolationKind && f.TurnId == turn.TurnId && f.Seat == seat);
        return new RuleViolationRecord(turn.TurnId, turn.Move ?? "-", fact?.Data?.GetValueOrDefault("code") ?? "-");
    }
}
