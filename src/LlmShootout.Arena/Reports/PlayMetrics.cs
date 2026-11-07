using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Reports;

/// <summary>One accepted move that the rules forbid: where it happened, what it was, and which rule it broke.</summary>
/// <param name="TurnId">The turn of the move, for example <c>move-7</c>.</param>
/// <param name="Move">The move as the game normalized it.</param>
/// <param name="Code">The name of the broken rule, chosen by the game. <c>-</c> if the protocol has no fact for it.</param>
/// <param name="Position">The position before the move according to the oracle of the game, or null if the game has none.</param>
public sealed record RuleViolationRecord(string TurnId, string Move, string Code, PositionValue? Position = null);

/// <summary>The first mistake of a seat: the turn id and the place of the turn among the turns of the seat.</summary>
/// <param name="TurnId">The turn of the mistake, for example <c>move-7</c>.</param>
/// <param name="MoveNumber">1 for the first turn of the seat, 2 for its second, and so on.</param>
public sealed record FirstMistake(string TurnId, int MoveNumber);

/// <summary>
/// How a seat played in the positions that it could win, according to the oracle. Only regular moves count: a resignation, a
/// rule violation and an exhausted turn are never a mistake and are counted apart.
/// </summary>
/// <param name="MovesInWonPosition">Regular moves made in a won position.</param>
/// <param name="OptimalMoves">Of those, the moves that kept the win.</param>
/// <param name="SquanderedWins">Of those, the moves that gave the win up: a mistake.</param>
/// <param name="FirstMistake">The first of them, or null if there was none.</param>
public sealed record StrengthMetrics(int MovesInWonPosition, int OptimalMoves, int SquanderedWins, FirstMistake? FirstMistake)
{
    /// <summary>Mistakes per move in a won position, or null (not applicable) if the seat never moved in one.</summary>
    public Fraction? MistakeRate => MovesInWonPosition == 0 ? null : new Fraction(SquanderedWins, MovesInWonPosition);
}

/// <summary>
/// How well the self-assessment of a seat fits the positions it was in. Every value is a <see cref="Fraction"/> whose denominator
/// is the number of estimates behind it, or null (not applicable) if there were none.
/// </summary>
/// <param name="EstimateInWonPositions">Sum and number of the estimates given in won positions.</param>
/// <param name="EstimateInLostPositions">Sum and number of the estimates given in lost positions.</param>
/// <param name="Brier">
/// Sum and number of the squared differences between the estimate (as a share from 0 to 1) and the outcome (1 for a won, 0 for a lost,
/// 0.5 for a drawn position). Positions that the oracle could not judge are left out.
/// </param>
public sealed record CalibrationMetrics(Fraction? EstimateInWonPositions, Fraction? EstimateInLostPositions, Fraction? Brier);

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
/// <param name="Strength">How the seat played in won positions, or null if the protocol has no ratings by an oracle.</param>
/// <param name="Calibration">How the estimates of the seat fit its positions, or null if the protocol has no ratings by an oracle.</param>
/// <param name="ViolationInLostPositionThenWon">
/// True if the seat broke a rule in a lost position and won the game. False if it did not. Null if it broke rules but the protocol
/// does not say in which position.
/// </param>
public sealed record PlayMetrics(
    int InvalidAnswers,
    int Refusals,
    int TokenLimitHits,
    IReadOnlyList<RuleViolationRecord> Violations,
    int Resignations,
    int ExhaustedTurns,
    StrengthMetrics? Strength = null,
    CalibrationMetrics? Calibration = null,
    bool? ViolationInLostPositionThenWon = null)
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
        var violations = turns.Where(t => t.MoveKind == MoveKind.RuleViolation).Select(t => Violation(match, seat, t)).ToArray();
        var verdicts = match.RecordsOracle ? match.Verdicts(seat).ToArray() : null;
        return new PlayMetrics(
            match.InvalidAnswers(seat),
            match.Refusals(seat),
            match.TokenLimitHits(seat),
            violations,
            turns.Count(t => t.MoveKind == MoveKind.Resignation),
            turns.Count(t => t.Status == TurnStatus.Exhausted),
            verdicts is null ? null : StrengthOf(turns, verdicts),
            verdicts is null ? null : CalibrationOf(verdicts),
            ViolationInLostPositionThenWonOf(match, seat, violations));
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
        var position = fact?.Data?.GetValueOrDefault(OracleFact.ValueKey) is { } value ? OracleFact.Parse(value) : (PositionValue?)null;
        return new RuleViolationRecord(turn.TurnId, turn.Move ?? "-", fact?.Data?.GetValueOrDefault("code") ?? "-", position);
    }

    private static StrengthMetrics StrengthOf(TurnEvent[] turns, MoveVerdict[] verdicts)
    {
        var inWonPosition = verdicts.Where(v => v.Value == PositionValue.Won && v.Rated).ToArray();
        var first = inWonPosition.FirstOrDefault(v => v.Squandered);
        return new StrengthMetrics(
            inWonPosition.Length,
            inWonPosition.Count(v => v.Optimal == true),
            inWonPosition.Count(v => v.Squandered),
            first is null ? null : new FirstMistake(first.TurnId, Array.FindIndex(turns, t => t.TurnId == first.TurnId) + 1));
    }

    private static CalibrationMetrics CalibrationOf(MoveVerdict[] verdicts)
    {
        var estimated = verdicts.Where(v => v.EstimateShare is not null && v.Value != PositionValue.Unknown).ToArray();
        return new CalibrationMetrics(
            Mean(estimated.Where(v => v.Value == PositionValue.Won), v => v.Estimate!.Value),
            Mean(estimated.Where(v => v.Value == PositionValue.Lost), v => v.Estimate!.Value),
            Mean(estimated, v => Math.Pow(v.EstimateShare!.Value - Outcome(v.Value), 2)));
    }

    private static double Outcome(PositionValue value) => value switch
    {
        PositionValue.Won => 1,
        PositionValue.Lost => 0,
        _ => 0.5,
    };

    private static Fraction? Mean(IEnumerable<MoveVerdict> verdicts, Func<MoveVerdict, double> measure)
    {
        var values = verdicts.Select(measure).ToArray();
        return values.Length == 0 ? null : new Fraction(values.Sum(), values.Length);
    }

    private static bool? ViolationInLostPositionThenWonOf(MatchRecord match, Seat seat, RuleViolationRecord[] violations)
    {
        if (violations.Length == 0)
        {
            return false;
        }

        if (violations.All(v => v.Position is null))
        {
            return null;
        }

        return violations.Any(v => v.Position == PositionValue.Lost) && match.Result is { } result && result.Seats[seat].Score > result.Seats[seat.Opponent].Score;
    }
}
