using System.Diagnostics;
using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>
/// One match of Split or Steal. With messages, the players first write them in turns; then both decide
/// at the same time and in secret between <c>split</c> and <c>steal</c>.
/// </summary>
public sealed class SplitOrStealGame : IGame
{
    /// <summary>The move that shares the pot.</summary>
    public const string Split = "split";

    /// <summary>The move that takes the whole pot if the other player shares it.</summary>
    public const string Steal = "steal";

    /// <summary>Kind of the public fact that reveals a decision, and the key of its data.</summary>
    public const string DecisionFact = "decision";

    /// <summary>The label of a seat that gave no valid decision.</summary>
    public const string NoDecision = "no_decision";

    /// <summary>The two moves.</summary>
    public static IReadOnlyList<string> Moves { get; } = [Split, Steal];

    /// <summary>The machine-readable outcomes of a game (<c>GameResult.Outcome</c>). A missing decision has its own outcome per seat.</summary>
    public static class Outcomes
    {
        /// <summary>Both split.</summary>
        public const string BothSplit = "both_split";

        /// <summary>Both stole.</summary>
        public const string BothSteal = "both_steal";

        /// <summary>A stole, B split.</summary>
        public const string AStole = "a_stole";

        /// <summary>B stole, A split.</summary>
        public const string BStole = "b_stole";

        /// <summary>Neither seat gave a valid decision.</summary>
        public const string NoDecisionBoth = "no_decision_both";

        /// <summary>A gave no valid decision.</summary>
        public const string NoDecisionA = "no_decision_a";

        /// <summary>B gave no valid decision.</summary>
        public const string NoDecisionB = "no_decision_b";
    }

    private readonly SplitOrStealOptions _options;
    private readonly GameSetup _setup;
    private readonly int _talkTurns;
    private int _talkDone;

    /// <summary>Creates the game.</summary>
    public SplitOrStealGame(GameSetup setup, SplitOrStealOptions options)
    {
        _setup = setup;
        _options = options;
        _talkTurns = setup.Communication ? 2 * options.MessagesPerPlayer : 0;
    }

    /// <inheritdoc />
    public GameResult? Result { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<TurnRequest> PendingTurns =>
        Result is not null ? [] : _talkDone < _talkTurns ? [TalkTurn()] : [.. Seat.All.Select(DecideTurn)];

    /// <inheritdoc />
    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of(
        _setup.Communication ? "rules.talk" : "rules.silent",
        ("pot", Text(_options.Pot)),
        ("half", Text(_options.Pot / 2)),
        ("messages", Text(_options.MessagesPerPlayer)));

    /// <inheritdoc />
    public TemplateRef ViewFor(Seat seat) => _talkDone < _talkTurns
        ? TemplateRef.Of("view.talk", ("done", Text(_talkDone)), ("total", Text(_talkTurns)), ("pot", Text(_options.Pot)))
        : TemplateRef.Of("view.decide", ("pot", Text(_options.Pot)));

    /// <inheritdoc />
    public MoveCheck Check(TurnRequest turn, string move)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(move);
        var normalized = move.Trim().ToLowerInvariant();
        return Moves.Contains(normalized)
            ? MoveCheck.Ok(normalized)
            : MoveCheck.Reject("not-allowed-value", TemplateRef.Of("error.move", ("move", move)), "\"split\", \"steal\"");
    }

    /// <inheritdoc />
    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        if (_talkDone < _talkTurns)
        {
            _talkDone++;
            return new TurnResolution([]);
        }

        var decisions = submissions.ToDictionary(s => s.Seat, s => s.Status == SubmissionStatus.Played ? s.Move : null);
        Result = Resolve(decisions[Seat.A], decisions[Seat.B]);
        return new TurnResolution([.. Seat.All.SelectMany(seat => Reveal(seat, decisions[seat], Result.Seats[seat]))]);
    }

    /// <inheritdoc />
    public string? SampleLegalMove(TurnRequest turn, SeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(rng);
        return turn.Kind == "decide" ? Moves[rng.NextInt(Moves.Count)] : null;
    }

    private TurnRequest TalkTurn()
    {
        var speaker = _talkDone % 2 == 0 ? _setup.FirstSeat : _setup.FirstSeat.Opponent;
        var own = _talkDone / 2 + 1;
        return new TurnRequest(
            $"talk-{_talkDone + 1}",
            "talk",
            speaker,
            FieldRule.Required,
            FieldRule.Forbidden,
            TemplateRef.Of("instruction.talk", ("own", Text(own)), ("messages", Text(_options.MessagesPerPlayer))));
    }

    private TurnRequest DecideTurn(Seat seat) => new(
        "decide",
        "decide",
        seat,
        FieldRule.Forbidden,
        FieldRule.Required,
        TemplateRef.Of("instruction.decide"),
        new MoveFormat(TemplateRef.Of("format.decide"), Moves));

    private GameResult Resolve(string? a, string? b)
    {
        var half = _options.Pot / 2;
        var (pa, pb, outcome) = (a, b) switch
        {
            (Split, Split) => (half, half, Outcomes.BothSplit),
            (Steal, Steal) => (0, 0, Outcomes.BothSteal),
            (Steal, Split) => (_options.Pot, 0, Outcomes.AStole),
            (Split, Steal) => (0, _options.Pot, Outcomes.BStole),
            (null, null) => (0, 0, Outcomes.NoDecisionBoth),
            (null, Split) => (0, half, Outcomes.NoDecisionA),
            (null, Steal) => (0, _options.Pot, Outcomes.NoDecisionA),
            (Split, null) => (half, 0, Outcomes.NoDecisionB),
            (Steal, null) => (_options.Pot, 0, Outcomes.NoDecisionB),
            _ => throw new UnreachableException($"Check lets only '{Split}' or '{Steal}' through, but the decisions are '{a}' and '{b}'."),
        };
        return new GameResult(
            outcome,
            new Dictionary<Seat, SeatResult> { [Seat.A] = new(pa, a ?? NoDecision), [Seat.B] = new(pb, b ?? NoDecision) },
            new Dictionary<string, string> { ["pot"] = Text(_options.Pot) });
    }

    private static IEnumerable<GameFact> Reveal(Seat seat, string? decision, SeatResult result)
    {
        yield return new GameFact(
            DecisionFact,
            FactAudience.Everyone,
            seat,
            TemplateRef.Of(decision is null ? "fact.no_decision" : "fact.decision", ("decision", decision ?? string.Empty)),
            new Dictionary<string, string> { [DecisionFact] = decision ?? NoDecision });
        yield return new GameFact(
            "payout",
            FactAudience.Everyone,
            seat,
            TemplateRef.Of("fact.payout", ("points", Text(result.Score))),
            new Dictionary<string, string> { ["points"] = Text(result.Score) });
    }

    private static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
