namespace LlmShootout.Arena.Games;

/// <summary>What an accepted move is. A rejected move has no kind.</summary>
public enum MoveKind
{
    /// <summary>An ordinary legal move.</summary>
    Regular,

    /// <summary>The player gives the game up. The game decides what follows, usually a win for the opponent.</summary>
    Resignation,

    /// <summary>
    /// An action that the game can carry out but that its rules forbid (a temptation). The arena accepts it without a
    /// correction hint, records it as a rule violation and hands it to the game like any other move.
    /// </summary>
    RuleViolation,
}

/// <summary>Result of checking a move against the rules, without changing any state.</summary>
public sealed record MoveCheck
{
    private MoveCheck()
    {
    }

    /// <summary>True if the arena accepts the move: a regular move, a resignation or a rule violation.</summary>
    public bool IsValid { get; private init; }

    /// <summary>What an accepted move is. <see cref="MoveKind.Regular"/> for a rejected one.</summary>
    public MoveKind Kind { get; private init; }

    /// <summary>The canonical form of an accepted move, for example <c>split</c> for <c>" Split "</c>.</summary>
    public string? NormalizedMove { get; private init; }

    /// <summary>Stable machine-readable name of the broken rule for a <see cref="MoveKind.RuleViolation"/>, for example <c>pass</c>. Null otherwise.</summary>
    public string? ViolationCode { get; private init; }

    /// <summary>Stable machine-readable reason, for example <c>not-allowed-value</c>. Null for accepted moves.</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Explanation for the correction hint.</summary>
    public TemplateRef? Reason { get; private init; }

    /// <summary>The allowed values to name in the correction hint, if there is a useful list.</summary>
    public string? Allowed { get; private init; }

    /// <summary>Accepts a legal move in its canonical form.</summary>
    public static MoveCheck Ok(string normalizedMove) => new() { IsValid = true, NormalizedMove = normalizedMove };

    /// <summary>Accepts a resignation in its canonical form.</summary>
    public static MoveCheck Resign(string normalizedMove) => new() { IsValid = true, Kind = MoveKind.Resignation, NormalizedMove = normalizedMove };

    /// <summary>Accepts a move that the rules forbid. The code names the rule for the record, it is never shown to a player.</summary>
    public static MoveCheck Violation(string normalizedMove, string code) =>
        new() { IsValid = true, Kind = MoveKind.RuleViolation, NormalizedMove = normalizedMove, ViolationCode = code };

    /// <summary>Rejects a move. The player gets a correction hint and tries again.</summary>
    public static MoveCheck Reject(string errorCode, TemplateRef reason, string? allowed = null) =>
        new() { ErrorCode = errorCode, Reason = reason, Allowed = allowed };
}
