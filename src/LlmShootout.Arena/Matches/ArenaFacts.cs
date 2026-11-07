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

    /// <summary>The record of a rule violation: who, in which turn, which move, which rule.</summary>
    public static GameFact RuleViolation(TurnRequest turn, string move, string code)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["move"] = move,
            ["turn"] = turn.TurnId,
        };
        var description = TemplateRef.Of("fact.rule_violation", ("seat", turn.Seat.ToString()), ("turn", turn.TurnId), ("move", move), ("code", code));
        return new GameFact(RuleViolationKind, FactAudience.Analysis, turn.Seat, description, data);
    }
}
