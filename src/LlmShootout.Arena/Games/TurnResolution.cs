namespace LlmShootout.Arena.Games;

/// <summary>What happened to a submitted turn: arena input for <see cref="IGame.Apply"/>.</summary>
public enum SubmissionStatus
{
    /// <summary>The player delivered a legal move (or none was required).</summary>
    Played,

    /// <summary>The player used up all attempts without a legal move. The game decides what that means.</summary>
    Exhausted,
}

/// <summary>An answered turn, handed to the game together with the other turns of the same round.</summary>
public sealed record TurnSubmission(string TurnId, Seat Seat, string? Move, SubmissionStatus Status);

/// <summary>Who may see a fact.</summary>
public enum FactAudience
{
    /// <summary>Both players and the report.</summary>
    Everyone,

    /// <summary>Only the seat named in <see cref="GameFact.Seat"/>, plus the report.</summary>
    Seat,

    /// <summary>Only the protocol and the report, never a prompt (for example a Nim oracle verdict).</summary>
    Analysis,
}

/// <summary>Something the game wants on record: a reveal, a feedback, a private roll, an analysis result.</summary>
/// <param name="Kind">Machine-readable kind, for example <c>reveal</c>, <c>feedback</c>, <c>oracle</c>.</param>
/// <param name="Audience">Who may see it.</param>
/// <param name="Seat">The seat the fact belongs to: the only viewer for <see cref="FactAudience.Seat"/>, otherwise the acting seat.</param>
/// <param name="Description">The fact as text template, rendered into prompts and reports.</param>
/// <param name="Data">Machine-readable details for the protocol and for analysis.</param>
public sealed record GameFact(
    string Kind,
    FactAudience Audience,
    Seat? Seat,
    TemplateRef Description,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>The facts produced by applying one round of turns.</summary>
public sealed record TurnResolution(IReadOnlyList<GameFact> Facts);

/// <summary>Outcome of one seat.</summary>
/// <param name="Score">Points, payout or result number.</param>
/// <param name="Label">Machine-readable label such as <c>win</c>, <c>split</c> or <c>no_decision</c>.</param>
public sealed record SeatResult(double Score, string Label);

/// <summary>Final result of a game.</summary>
/// <param name="Outcome">Machine-readable outcome of the whole game, for example <c>both_split</c>.</param>
/// <param name="Seats">Result per seat.</param>
/// <param name="Metrics">Further numbers for the report.</param>
public sealed record GameResult(
    string Outcome,
    IReadOnlyDictionary<Seat, SeatResult> Seats,
    IReadOnlyDictionary<string, string>? Metrics = null);
