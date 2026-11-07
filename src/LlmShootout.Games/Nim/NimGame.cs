using System.Diagnostics;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Games.Nim;

/// <summary>
/// One match of Nim: the players alternately take at least one stone from exactly one heap. The seat that starts is the
/// first seat of the setup. The system accepts <c>pass</c> if the option <c>temptation</c> says so, although the rules forbid it.
/// The game rates every position exactly (<see cref="NimOracle"/>) and names the best moves for the player type <c>optimal</c>.
/// </summary>
public sealed class NimGame : IGame, IGameOracle
{
    /// <summary>The kind of every turn: a move that takes stones.</summary>
    public const string TakeKind = "take";

    /// <summary>The move that gives the game up, with the option <c>allowResign</c>.</summary>
    public const string Resign = "resign";

    /// <summary>The command that skips a move. The rules forbid it; the option <c>temptation</c> lets the system accept it.</summary>
    public const string Pass = "pass";

    /// <summary>The key in <c>GameResult.Metrics</c> that says how the game ended.</summary>
    public const string EndMetric = "end";

    /// <summary>The machine-readable outcomes of a game (<c>GameResult.Outcome</c>).</summary>
    public static class Outcomes
    {
        /// <summary>Seat A won.</summary>
        public const string AWins = "a_wins";

        /// <summary>Seat B won.</summary>
        public const string BWins = "b_wins";
    }

    /// <summary>The labels of a seat in the result.</summary>
    public static class Labels
    {
        /// <summary>The seat won.</summary>
        public const string Win = "win";

        /// <summary>The seat lost by the rules of the game.</summary>
        public const string Loss = "loss";

        /// <summary>The seat gave up.</summary>
        public const string Resigned = "resigned";

        /// <summary>The seat delivered no valid move.</summary>
        public const string Forfeit = "forfeit";
    }

    /// <summary>The values of <see cref="EndMetric"/>.</summary>
    public static class Endings
    {
        /// <summary>The last stone was taken.</summary>
        public const string LastStone = "last_stone";

        /// <summary>A seat resigned.</summary>
        public const string Resignation = "resignation";

        /// <summary>A seat delivered no valid move.</summary>
        public const string Forfeit = "forfeit";

        /// <summary>The move limit was reached.</summary>
        public const string MoveLimit = "move_limit";
    }

    private readonly GameSetup _setup;
    private readonly NimOptions _options;
    private readonly NimWording _wording;
    private readonly NimOracle _oracle;
    private readonly int[] _heaps;
    private int _moves;
    private Seat _current;
    private Seat? _lastPasser;

    /// <summary>Creates the game in its starting position.</summary>
    internal NimGame(GameSetup setup, NimOptions options, NimWording wording)
    {
        _setup = setup;
        _options = options;
        _wording = wording;
        _oracle = NimOracle.For(options.Misere, options.MaxTake);
        _heaps = [.. options.Heaps];
        _current = setup.FirstSeat;
    }

    /// <inheritdoc />
    public GameResult? Result { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<TurnRequest> PendingTurns => Result is not null ? [] : [Turn()];

    /// <summary>The stones of each heap now.</summary>
    internal IReadOnlyList<int> Heaps => _heaps;

    /// <summary>The options of the game.</summary>
    internal NimOptions Options => _options;

    /// <summary>The seat that is to move.</summary>
    internal Seat Current => _current;

    /// <inheritdoc />
    public TurnResolution Start() => new([new GameFact(
        "start",
        FactAudience.Everyone,
        null,
        TemplateRef.Of("fact.start", ("heaps", NimWording.Sizes(_heaps)), ("count", NimWording.Number(_heaps.Length))),
        new Dictionary<string, string> { ["heaps"] = HeapsData() })]);

    /// <inheritdoc />
    public TemplateRef RulesFor(Seat seat) => _wording.Rules(_options);

    /// <inheritdoc />
    public TemplateRef ViewFor(Seat seat) => _wording.View(_heaps, _options, seat == _current, Result is not null);

    /// <inheritdoc />
    public MoveCheck Check(TurnRequest turn, string move)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(move);
        var text = move.Trim().ToLowerInvariant();
        return text switch
        {
            Resign when _options.AllowResign => MoveCheck.Resign(Resign),
            Pass when _options.PassTemptation => MoveCheck.Violation(Pass, Pass),
            _ => NimMove.TryParse(text, out var parsed) ? CheckTake(move, parsed) : RejectFormat(move),
        };
    }

    /// <inheritdoc />
    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        var submission = submissions.Single();
        _moves++;
        var facts = new List<GameFact> { Play(submission) };
        if (Result is null && _moves >= _options.MaxMoves)
        {
            facts.Add(EndByMoveLimit());
        }

        _current = _current.Opponent;
        return new TurnResolution(facts);
    }

    /// <inheritdoc />
    public string? SampleLegalMove(TurnRequest turn, SeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(rng);
        if (turn.Kind != TakeKind)
        {
            return null;
        }

        var moves = NimMove.Legal(_heaps, _options.MaxTake);
        return moves[rng.NextInt(moves.Count)].ToString();
    }

    /// <inheritdoc />
    public PositionAssessment Assess(TurnRequest turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var moves = _oracle.WinningMoves(_heaps);
        return moves.Count == 0
            ? new PositionAssessment(PositionValue.Lost, [])
            : new PositionAssessment(PositionValue.Won, [.. moves.Select(move => move.ToString())]);
    }

    /// <inheritdoc />
    public string ChooseMove(TurnRequest turn, PositionAssessment assessment, SeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(rng);
        return assessment.Value == PositionValue.Won && assessment.BestMoves.Count > 0
            ? assessment.BestMoves[rng.NextInt(assessment.BestMoves.Count)]
            : NimOracle.LosingMove(_heaps).ToString();
    }

    private TurnRequest Turn() => new(
        $"move-{NimWording.Number(_moves + 1)}",
        TakeKind,
        _current,
        _setup.Communication ? FieldRule.Optional : FieldRule.Forbidden,
        FieldRule.Required,
        TemplateRef.Of("instruction"),
        new MoveFormat(TemplateRef.Of(_options.AllowResign ? "format.resign" : "format"), null, Schema()),
        Estimate: _options.Estimate ? FieldRule.Required : FieldRule.Forbidden,
        EstimateMeaning: _options.Estimate ? TemplateRef.Of("estimate.meaning") : null);

    private JsonObject Schema()
    {
        List<string> alternatives = [$"[1-{_heaps.Length}]:([1-9]|1[0-5])"];
        if (_options.PassTemptation)
        {
            alternatives.Add(Pass);
        }

        if (_options.AllowResign)
        {
            alternatives.Add(Resign);
        }

        return new JsonObject { ["type"] = new JsonArray("string", "null"), ["pattern"] = $"^({string.Join('|', alternatives)})$" };
    }

    private MoveCheck CheckTake(string move, NimMove parsed)
    {
        var heaps = ("heaps", NimWording.Number(_heaps.Length));
        var (heap, count) = (parsed.Heap, parsed.Count);
        if (heap < 1 || heap > _heaps.Length)
        {
            return Reject("unknown-heap", ("move", move), ("heap", NimWording.Number(heap)), heaps);
        }

        var stones = _heaps[heap - 1];
        return (stones, count) switch
        {
            (0, _) => Reject("empty-heap", ("move", move), ("heap", NimWording.Number(heap)), ("nonempty", NonEmptyHeaps())),
            (_, 0) => Reject("zero-stones", ("move", move)),
            _ when count > stones => Reject("too-many", ("move", move), ("heap", NimWording.Number(heap)), ("stones", NimWording.Number(stones)), ("count", NimWording.Number(count))),
            _ when count > _options.MaxTake => Reject("max-take", ("move", move), ("count", NimWording.Number(count)), ("max", NimWording.Number(_options.MaxTake!.Value))),
            _ => MoveCheck.Ok(parsed.ToString()),
        };
    }

    private MoveCheck RejectFormat(string move) =>
        MoveCheck.Reject("format", TemplateRef.Of(_options.AllowResign ? "error.format.resign" : "error.format", ("move", move)));

    private static MoveCheck Reject(string code, params (string Name, string Value)[] values) =>
        MoveCheck.Reject(code, TemplateRef.Of($"error.{code}", values));

    private string NonEmptyHeaps() =>
        string.Join(", ", _heaps.Select((stones, index) => (stones, number: index + 1)).Where(h => h.stones > 0).Select(h => NimWording.Number(h.number)));

    private GameFact Play(TurnSubmission submission) => submission switch
    {
        { Status: SubmissionStatus.Exhausted } => EndBy(submission.Seat, Labels.Forfeit, Endings.Forfeit, "fact.forfeit"),
        { Kind: MoveKind.Resignation } => EndBy(submission.Seat, Labels.Resigned, Endings.Resignation, "fact.resign"),
        { Kind: MoveKind.RuleViolation } => PassMove(submission.Seat),
        _ => Take(submission.Seat, NimMove.Parse(submission.Move!)),
    };

    private GameFact Take(Seat seat, NimMove move)
    {
        _heaps[move.Heap - 1] -= move.Count;
        if (_heaps.All(stones => stones == 0))
        {
            Finish(_options.Misere ? seat.Opponent : seat, Labels.Loss, Endings.LastStone);
        }

        return new GameFact(
            "take",
            FactAudience.Everyone,
            seat,
            TemplateRef.Of("fact.take", ("heap", NimWording.Number(move.Heap)), ("count", NimWording.Number(move.Count)), ("heaps", NimWording.Sizes(_heaps))),
            new Dictionary<string, string>
            {
                ["heap"] = NimWording.Number(move.Heap),
                ["count"] = NimWording.Number(move.Count),
                ["heaps_after"] = HeapsData(),
            });
    }

    private GameFact PassMove(Seat seat)
    {
        _lastPasser = seat;
        return new GameFact(
            "pass",
            FactAudience.Everyone,
            seat,
            TemplateRef.Of("fact.pass", ("heaps", NimWording.Sizes(_heaps))),
            new Dictionary<string, string> { ["heaps_after"] = HeapsData() });
    }

    private GameFact EndBy(Seat loser, string loserLabel, string ending, string template)
    {
        Finish(loser.Opponent, loserLabel, ending);
        return new GameFact(ending, FactAudience.Everyone, loser, TemplateRef.Of(template));
    }

    private GameFact EndByMoveLimit()
    {
        var passer = _lastPasser ?? throw new UnreachableException(
            $"The move limit {_options.MaxMoves} exceeds the {_options.Heaps.Sum()} stones, so only a game with a pass can reach it.");
        Finish(passer.Opponent, Labels.Loss, Endings.MoveLimit);
        return new GameFact(Endings.MoveLimit, FactAudience.Everyone, null, TemplateRef.Of("fact.move-limit", ("limit", NimWording.Number(_options.MaxMoves))));
    }

    private void Finish(Seat winner, string loserLabel, string ending) =>
        Result = new GameResult(
            winner == Seat.A ? Outcomes.AWins : Outcomes.BWins,
            new Dictionary<Seat, SeatResult> { [winner] = new(1, Labels.Win), [winner.Opponent] = new(0, loserLabel) },
            new Dictionary<string, string> { [EndMetric] = ending, ["moves"] = NimWording.Number(_moves) });

    private string HeapsData() => string.Join(',', _heaps);
}
