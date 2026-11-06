namespace LlmShootout.Arena.Games;

/// <summary>Result of checking a move against the rules, without changing any state.</summary>
public sealed record MoveCheck
{
    private MoveCheck()
    {
    }

    /// <summary>True if the move is legal.</summary>
    public bool IsValid { get; private init; }

    /// <summary>The canonical form of a legal move, for example <c>split</c> for <c>" Split "</c>.</summary>
    public string? NormalizedMove { get; private init; }

    /// <summary>Stable machine-readable reason, for example <c>not-allowed-value</c>. Null for legal moves.</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Explanation for the correction hint.</summary>
    public TemplateRef? Reason { get; private init; }

    /// <summary>The allowed values to name in the correction hint, if there is a useful list.</summary>
    public string? Allowed { get; private init; }

    /// <summary>Accepts a move in its canonical form.</summary>
    public static MoveCheck Ok(string normalizedMove) => new() { IsValid = true, NormalizedMove = normalizedMove };

    /// <summary>Rejects a move.</summary>
    public static MoveCheck Reject(string errorCode, TemplateRef reason, string? allowed = null) =>
        new() { ErrorCode = errorCode, Reason = reason, Allowed = allowed };
}
