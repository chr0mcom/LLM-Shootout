using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Games;

/// <summary>How a seat stands before its move, if both sides play by the rules from here on.</summary>
public enum PositionValue
{
    /// <summary>The oracle cannot tell.</summary>
    Unknown,

    /// <summary>The seat can force a win.</summary>
    Won,

    /// <summary>The opponent can force a win.</summary>
    Lost,

    /// <summary>Neither side can force a win.</summary>
    Drawn,
}

/// <summary>What the oracle says about a position.</summary>
/// <param name="Value">The value for the seat that is to move.</param>
/// <param name="BestMoves">
/// All regular moves, normalized like the moves of <see cref="IGame.Check"/>, that keep <paramref name="Value"/>, in a fixed
/// order. Empty in a lost or unknown position.
/// </param>
public sealed record PositionAssessment(PositionValue Value, IReadOnlyList<string> BestMoves);

/// <summary>
/// An optional capability of a running game: it can say how a seat stands before a move and which moves are best.
/// The game implements it in addition to <see cref="IGame"/> and holds no extra state for it.
/// </summary>
/// <remarks>
/// The arena asks for the assessment once per pending turn, before the answers of the round are collected, and records one
/// <c>oracle</c> fact per played move (see <see cref="OracleFact"/>). The fact is for the analysis only: no prompt and no
/// debriefing ever shows it. The same assessment feeds the player type <c>optimal</c>.
/// </remarks>
public interface IGameOracle
{
    /// <summary>
    /// Assesses the turn from the point of view of the seat that is to move, before it moves. The assessment assumes that
    /// both sides play by the rules from here on; moves that the rules forbid are not part of it.
    /// </summary>
    PositionAssessment Assess(TurnRequest turn);

    /// <summary>
    /// Chooses the move of the player type <c>optimal</c>: one of the best moves in a won or drawn position (a tie is
    /// decided with <paramref name="rng"/>), and a fixed, documented move in a lost or unknown position.
    /// </summary>
    string ChooseMove(TurnRequest turn, PositionAssessment assessment, SeededRng rng);
}

/// <summary>The name of the oracle fact, its keys and values. The arena writes it, reports and the series read it.</summary>
public static class OracleFact
{
    /// <summary>Kind of the fact that rates one move.</summary>
    public const string Kind = "oracle";

    /// <summary>Key: the position before the move, <c>won</c>, <c>lost</c>, <c>drawn</c> or <c>unknown</c>.</summary>
    public const string ValueKey = "value";

    /// <summary>Key: the move as the game normalized it.</summary>
    public const string MoveKey = "move";

    /// <summary>Key: the best moves, separated by commas, at most <see cref="MaxBestMoves"/>.</summary>
    public const string BestKey = "best";

    /// <summary>Key: whether the move was one of the best moves: <see cref="Yes"/>, <see cref="No"/> or <see cref="NotApplicable"/>.</summary>
    public const string OptimalKey = "optimal";

    /// <summary>Key: <see cref="Yes"/> for a regular move, <see cref="No"/> for a resignation or a rule violation, which are not rated.</summary>
    public const string RatedKey = "rated";

    /// <summary>Key: the turn that was rated.</summary>
    public const string TurnKey = "turn";

    /// <summary>The value of a flag that holds.</summary>
    public const string Yes = "true";

    /// <summary>The value of a flag that does not hold.</summary>
    public const string No = "false";

    /// <summary>The value of <see cref="OptimalKey"/> when the move is not compared with the best moves.</summary>
    public const string NotApplicable = "n/a";

    /// <summary>The most best moves that one fact names.</summary>
    public const int MaxBestMoves = 20;

    /// <summary>The text of a position value in the fact: <c>won</c>, <c>lost</c>, <c>drawn</c> or <c>unknown</c>.</summary>
    public static string Name(PositionValue value) => value switch
    {
        PositionValue.Won => "won",
        PositionValue.Lost => "lost",
        PositionValue.Drawn => "drawn",
        _ => "unknown",
    };

    /// <summary>Reads the text of a position value. Anything that is not known counts as <see cref="PositionValue.Unknown"/>.</summary>
    public static PositionValue Parse(string? text) => text switch
    {
        "won" => PositionValue.Won,
        "lost" => PositionValue.Lost,
        "drawn" => PositionValue.Drawn,
        _ => PositionValue.Unknown,
    };
}

/// <summary>
/// The rating of one played move, read from the protocol: the position before it, the best moves, whether the move was one
/// of them, and the self-assessment that came with the move.
/// </summary>
/// <param name="TurnId">The turn of the move.</param>
/// <param name="Seat">The seat that moved.</param>
/// <param name="Value">The position before the move, for the seat that moved.</param>
/// <param name="Move">The move as the game normalized it.</param>
/// <param name="Best">The best moves named by the oracle, at most <see cref="OracleFact.MaxBestMoves"/>.</param>
/// <param name="Optimal">True if the move was one of the best moves, false if it was not, null if the move is not compared with them.</param>
/// <param name="Rated">False for a resignation and a rule violation: they are neither a good nor a bad regular move.</param>
/// <param name="Kind">What the move was.</param>
/// <param name="Estimate">The self-assessment from 0 to 100 that came with the move, or null.</param>
public sealed record MoveVerdict(
    string TurnId,
    Seat Seat,
    PositionValue Value,
    string Move,
    IReadOnlyList<string> Best,
    bool? Optimal,
    bool Rated,
    MoveKind Kind = MoveKind.Regular,
    int? Estimate = null)
{
    /// <summary>True if the seat could force a win and played a regular move that gave this up.</summary>
    public bool Squandered => Value == PositionValue.Won && Rated && Optimal == false;

    /// <summary>Reads the rating from an <c>oracle</c> fact. The kind of move and the estimate belong to the turn and are added by the reader.</summary>
    public static MoveVerdict From(FactEvent fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Kind != OracleFact.Kind || fact.Seat is not { } seat)
        {
            throw new ArgumentException($"A fact of kind '{fact.Kind}' is not the rating of a move.", nameof(fact));
        }

        string Get(string key) => fact.Data?.GetValueOrDefault(key) ?? string.Empty;
        var best = Get(OracleFact.BestKey);
        return new MoveVerdict(
            fact.TurnId,
            seat,
            OracleFact.Parse(Get(OracleFact.ValueKey)),
            Get(OracleFact.MoveKey),
            best.Length == 0 ? [] : [.. best.Split(',')],
            Get(OracleFact.OptimalKey) switch
            {
                OracleFact.Yes => true,
                OracleFact.No => false,
                _ => null,
            },
            Get(OracleFact.RatedKey) != OracleFact.No);
    }

    /// <summary>The estimate as a share between 0 and 1, or null without an estimate.</summary>
    public double? EstimateShare => Estimate is { } e ? e / 100.0 : null;
}
