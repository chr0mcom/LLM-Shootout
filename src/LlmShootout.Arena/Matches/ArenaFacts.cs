using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Matches;

/// <summary>
/// Facts that the arena records itself, worded with the templates of the arena. They all have the audience
/// <see cref="FactAudience.Analysis"/>: they are in the protocol and the report, never in a prompt or a debriefing.
/// </summary>
public static class ArenaFacts
{
    /// <summary>Kind of the fact that records an accepted move which the rules forbid.</summary>
    public const string RuleViolationKind = "rule_violation";

    /// <summary>
    /// The record of a rule violation: who, in which turn, which move, which rule, and, if the game has an oracle, the position
    /// before the move.
    /// </summary>
    public static GameFact RuleViolation(TurnRequest turn, string move, string code, PositionValue? position = null)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["move"] = move,
            ["turn"] = turn.TurnId,
        };
        if (position is { } value)
        {
            data[OracleFact.ValueKey] = OracleFact.Name(value);
        }

        var description = TemplateRef.Of("fact.rule_violation", ("seat", turn.Seat.ToString()), ("turn", turn.TurnId), ("move", move), ("code", code));
        return new GameFact(RuleViolationKind, FactAudience.Analysis, turn.Seat, description, data);
    }

    /// <summary>
    /// The rating of one played move: the position before it, the move, the best moves and whether the move was one of them.
    /// A resignation and a rule violation are not rated and are not compared with the best moves.
    /// </summary>
    public static GameFact Oracle(TurnRequest turn, PositionAssessment assessment, string move, MoveKind kind)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(assessment);
        var best = string.Join(',', assessment.BestMoves.Take(OracleFact.MaxBestMoves));
        var rated = kind == MoveKind.Regular;
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OracleFact.ValueKey] = OracleFact.Name(assessment.Value),
            [OracleFact.MoveKey] = move,
            [OracleFact.BestKey] = best,
            [OracleFact.OptimalKey] = Optimality(assessment, move, rated),
            [OracleFact.RatedKey] = rated ? OracleFact.Yes : OracleFact.No,
            [OracleFact.TurnKey] = turn.TurnId,
        };
        var description = TemplateRef.Of(
            "fact.oracle",
            ("seat", turn.Seat.ToString()),
            ("turn", turn.TurnId),
            ("value", data[OracleFact.ValueKey]),
            ("move", move),
            ("optimal", data[OracleFact.OptimalKey]),
            ("best", best.Length == 0 ? "-" : best.Replace(",", ", ", StringComparison.Ordinal)));
        return new GameFact(OracleFact.Kind, FactAudience.Analysis, turn.Seat, description, data);
    }

    private static string Optimality(PositionAssessment assessment, string move, bool rated)
    {
        if (!rated || assessment.Value is not (PositionValue.Won or PositionValue.Drawn))
        {
            return OracleFact.NotApplicable;
        }

        return assessment.BestMoves.Contains(move, StringComparer.Ordinal) ? OracleFact.Yes : OracleFact.No;
    }
}
