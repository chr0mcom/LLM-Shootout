using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>
/// One match of Split or Steal. With communication, the players first exchange messages in turns; then both decide
/// at the same time and in secret between <c>split</c> and <c>steal</c>.
/// </summary>
public sealed class SplitOrStealGame : IGame
{
    /// <summary>The two moves.</summary>
    public static IReadOnlyList<string> Moves { get; } = ["split", "steal"];

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
            ("split", "split") => (half, half, "both_split"),
            ("steal", "steal") => (0, 0, "both_steal"),
            ("steal", "split") => (_options.Pot, 0, "a_stole"),
            ("split", "steal") => (0, _options.Pot, "b_stole"),
            (null, null) => (0, 0, "no_decision_both"),
            (null, "split") => (0, half, "no_decision_a"),
            (null, "steal") => (0, _options.Pot, "no_decision_a"),
            ("split", null) => (half, 0, "no_decision_b"),
            _ => (_options.Pot, 0, "no_decision_b"),
        };
        return new GameResult(
            outcome,
            new Dictionary<Seat, SeatResult> { [Seat.A] = new(pa, a ?? "no_decision"), [Seat.B] = new(pb, b ?? "no_decision") },
            new Dictionary<string, string> { ["pot"] = Text(_options.Pot) });
    }

    private static IEnumerable<GameFact> Reveal(Seat seat, string? decision, SeatResult result)
    {
        yield return new GameFact(
            "decision",
            FactAudience.Everyone,
            seat,
            TemplateRef.Of(decision is null ? "fact.no_decision" : "fact.decision", ("decision", decision ?? string.Empty)),
            new Dictionary<string, string> { ["decision"] = decision ?? "no_decision" });
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
