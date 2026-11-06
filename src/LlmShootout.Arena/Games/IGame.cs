using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Games;

/// <summary>
/// One running game between two seats. The arena drives it; the game owns the rules and the hidden state.
/// An instance lives for exactly one match.
/// </summary>
/// <remarks>
/// Round protocol: the arena reads <see cref="PendingTurns"/>, asks every listed seat for an answer, checks each
/// move with <see cref="Check"/>, and then hands all answers of the round to <see cref="Apply"/> at once.
/// With one pending turn the game is sequential, with two it is simultaneous: nobody sees the other move before
/// <see cref="Apply"/> has run. With zero pending turns the game is over and <see cref="Result"/> is set.
/// </remarks>
public interface IGame
{
    /// <summary>
    /// Called once before the first round. Return the hidden starting state of the seats as private facts
    /// (the dice of the first round, a drawn code, a random ship layout), so that it is in the protocol, in later
    /// prompts and in the debriefing. The default reports nothing.
    /// </summary>
    TurnResolution Start() => new([]);

    /// <summary>The turns that must be answered before the game advances. Empty when the game is over.</summary>
    IReadOnlyList<TurnRequest> PendingTurns { get; }

    /// <summary>The final result, or null while the game is running.</summary>
    GameResult? Result { get; }

    /// <summary>The rules and the role of a seat, shown once in the system prompt.</summary>
    TemplateRef RulesFor(Seat seat);

    /// <summary>
    /// The current state as this seat may see it (a board, own dice, a score). History is not part of the view:
    /// the arena builds it from the public facts and messages, so no game keeps a second history.
    /// </summary>
    TemplateRef ViewFor(Seat seat);

    /// <summary>Checks a move against the rules without changing state. Called only with a non-null move.</summary>
    MoveCheck Check(TurnRequest turn, string move);

    /// <summary>
    /// Applies all answers of one round. Moves are already normalized by <see cref="Check"/>. Returns the facts
    /// to record, for example the revealed decisions or an oracle verdict.
    /// </summary>
    TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions);

    /// <summary>Returns a legal move for a turn using the given generator, or null if none is required. For the random player.</summary>
    string? SampleLegalMove(TurnRequest turn, SeededRng rng);
}
