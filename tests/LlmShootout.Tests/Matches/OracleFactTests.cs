using System.Text.Json.Nodes;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Tests.Debriefing;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Matches;

public sealed class OracleFactTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly TakeAwayDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Heap(int stones, bool temptation = false)
    {
        var options = new JsonObject { ["stones"] = stones };
        if (temptation)
        {
            options["temptation"] = "pass";
        }

        return options;
    }

    private static ScriptedPlayer Moves(params string[] moves)
    {
        var player = new ScriptedPlayer();
        foreach (var move in moves)
        {
            player.Reply(MatchHarness.Answer(move: move));
        }

        return player;
    }

    private async Task<MatchRecord> PlayAsync(IGameDefinition definition, ScriptedPlayer a, ScriptedPlayer b, JsonObject options, bool debrief = false)
    {
        var (outcome, _) = await _harness.RunAsync(definition, a, b, gameOptions: options, debrief: debrief);
        return MatchRecord.Load(outcome.Directory);
    }

    private static FactEvent[] OracleFacts(MatchRecord match) => [.. match.Facts.Where(f => f.Kind == OracleFact.Kind)];

    [Theory]
    [InlineData(1, PositionValue.Won, "1")]
    [InlineData(3, PositionValue.Won, "3")]
    [InlineData(4, PositionValue.Lost, "")]
    [InlineData(6, PositionValue.Won, "2")]
    [InlineData(8, PositionValue.Lost, "")]
    [InlineData(11, PositionValue.Won, "3")]
    public void The_take_away_oracle_calls_a_heap_lost_exactly_when_it_is_a_multiple_of_four(int stones, PositionValue value, string best)
    {
        var game = _definition.Create(new GameSetup(1, false, "en", Seat.A, Heap(stones)));

        var assessment = ((IGameOracle)game).Assess(game.PendingTurns[0]);

        Assert.Equal((value, best), (assessment.Value, string.Join(',', assessment.BestMoves)));
    }

    [Fact]
    public void With_several_winning_moves_the_oracle_names_all_of_them_in_a_fixed_order()
    {
        var options = new JsonObject { ["stones"] = 3, ["takes"] = new JsonArray(4, 3, 1) };
        var game = (IGameOracle)_definition.Create(new GameSetup(1, false, "en", Seat.A, options));

        var assessment = game.Assess(((IGame)game).PendingTurns[0]);

        Assert.Equal(["1", "3"], assessment.BestMoves);
    }

    [Fact]
    public async Task Every_played_move_is_rated_in_an_analysis_fact_with_the_position_before_it()
    {
        var match = await PlayAsync(_definition, Moves("1", "1"), Moves("3"), Heap(5));

        var facts = OracleFacts(match);

        Assert.Equal(["move-1", "move-2", "move-3"], facts.Select(f => f.TurnId));
        Assert.All(facts, f => Assert.Equal(FactAudience.Analysis, f.Audience));
        Assert.Equal([Seat.A, Seat.B, Seat.A], facts.Select(f => f.Seat));
        Assert.Equal(
            new Dictionary<string, string> { ["value"] = "won", ["move"] = "1", ["best"] = "1", ["optimal"] = "true", ["rated"] = "true", ["turn"] = "move-1" },
            facts[0].Data);
        Assert.Equal(
            new Dictionary<string, string> { ["value"] = "lost", ["move"] = "3", ["best"] = string.Empty, ["optimal"] = "n/a", ["rated"] = "true", ["turn"] = "move-2" },
            facts[1].Data);
        Assert.Equal("Oracle, seat A, move-1: position won, move `1`, optimal: true. Best moves: 1.", facts[0].Text);
        Assert.Equal("Oracle, seat B, move-2: position lost, move `3`, optimal: n/a. Best moves: -.", facts[1].Text);
    }

    [Fact]
    public async Task The_fact_comes_after_the_turn_and_before_the_game_applies_the_move()
    {
        var match = await PlayAsync(_definition, Moves("1", "1"), Moves("3"), Heap(5));

        var order = match.Events.Select(e => e switch
        {
            TurnEvent { TurnId: "move-1" } => "turn",
            FactEvent { Kind: "oracle", TurnId: "move-1" } => "oracle",
            FactEvent { Kind: "take", TurnId: "move-1" } => "applied",
            _ => string.Empty,
        }).Where(x => x.Length > 0);

        Assert.Equal(["turn", "oracle", "applied"], order);
    }

    [Fact]
    public async Task The_fact_is_worded_in_the_language_of_the_match()
    {
        var run = await _harness.RunWithTokenAsync(_definition, Moves("1", "1"), Moves("3"), false, null, 7, Heap(5), Seat.A, TestContext.Current.CancellationToken, "de");

        var fact = run.Events.OfType<FactEvent>().First(f => f.Kind == OracleFact.Kind);

        Assert.Equal("Orakel, Sitz A, move-1: Lage won, Zug `1`, optimal: true. Beste Züge: 1.", fact.Text);
    }

    [Fact]
    public async Task A_move_that_gives_up_a_won_position_is_squandered_and_the_first_one_counts()
    {
        var match = await PlayAsync(_definition, Moves("1", "3"), Moves("1", "1"), Heap(6));

        var a = match.Verdicts(Seat.A).ToArray();
        var b = match.Verdicts(Seat.B).ToArray();

        Assert.Equal([PositionValue.Won, PositionValue.Lost], a.Select(v => v.Value));
        Assert.Equal([false, null], a.Select(v => v.Optimal));
        Assert.Equal([true, false], a.Select(v => v.Squandered));
        Assert.Equal(["2"], a[0].Best);
        Assert.Equal([PositionValue.Won, PositionValue.Won], b.Select(v => v.Value));
        Assert.Equal([true, true], b.Select(v => v.Optimal));
        Assert.DoesNotContain(b, v => v.Squandered);
    }

    [Fact]
    public async Task The_verdicts_carry_the_kind_of_move_and_the_estimate_of_their_turn()
    {
        var options = Heap(5);
        options["estimate"] = "required";
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "1", estimate: 90)).Reply(MatchHarness.Answer(move: "resign", estimate: 5));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "3", estimate: 10));

        var match = await PlayAsync(_definition, a, b, options);

        var verdicts = match.Verdicts(Seat.A).ToArray();
        Assert.Equal([90, 5], verdicts.Select(v => v.Estimate));
        Assert.Equal([MoveKind.Regular, MoveKind.Resignation], verdicts.Select(v => v.Kind));
        Assert.Equal([0.9, 0.05], verdicts.Select(v => v.EstimateShare));
        Assert.Equal([10], match.Verdicts(Seat.B).Select(v => v.Estimate));
    }

    [Fact]
    public async Task A_resignation_and_a_rule_violation_are_not_rated_and_never_count_as_a_mistake()
    {
        var match = await PlayAsync(_definition, Moves("pass"), Moves("resign"), Heap(5, temptation: true));

        var facts = OracleFacts(match);
        var verdicts = Seat.All.SelectMany(match.Verdicts).ToArray();

        Assert.Equal(["won", "won"], facts.Select(f => f.Data!["value"]));
        Assert.Equal(["n/a", "n/a"], facts.Select(f => f.Data!["optimal"]));
        Assert.Equal(["false", "false"], facts.Select(f => f.Data!["rated"]));
        Assert.Equal([MoveKind.RuleViolation, MoveKind.Resignation], verdicts.Select(v => v.Kind));
        Assert.All(verdicts, v => Assert.False(v.Squandered));
        Assert.All(verdicts, v => Assert.False(v.Rated));
    }

    [Fact]
    public async Task The_rule_violation_fact_names_the_position_before_the_move()
    {
        var match = await PlayAsync(_definition, Moves("pass", "2"), Moves("3"), Heap(5, temptation: true));

        var violation = Assert.Single(match.Facts, f => f.Kind == ArenaFacts.RuleViolationKind);

        Assert.Equal(new Dictionary<string, string> { ["code"] = "pass", ["move"] = "pass", ["turn"] = "move-1", ["value"] = "won" }, violation.Data);
    }

    [Fact]
    public async Task A_turn_that_used_up_all_attempts_has_no_rating()
    {
        var match = await PlayAsync(_definition, Moves("9", "9", "9"), new ScriptedPlayer(), Heap(5));

        Assert.Empty(OracleFacts(match));
        Assert.Equal("b_wins", match.Result!.Outcome);
    }

    [Fact]
    public async Task A_game_without_an_oracle_writes_no_rating_and_has_no_verdicts()
    {
        var (outcome, _) = await _harness.RunAsync(new NumberGuessDefinition(), new RandomPlayer(1), new RandomPlayer(2));
        var match = MatchRecord.Load(outcome.Directory);

        Assert.Empty(OracleFacts(match));
        Assert.False(match.RecordsOracle);
        Assert.Empty(match.Verdicts(Seat.A));
    }

    [Fact]
    public async Task No_prompt_and_no_debriefing_ever_shows_a_rating()
    {
        var debrief = SplitOrStealMatchTests.DebriefJson();
        var a = Moves("1", "1").Reply(debrief);
        var b = Moves("3").Reply(debrief);

        var match = await PlayAsync(_definition, a, b, Heap(5), debrief: true);

        var prompts = string.Join("\n", match.Calls.SelectMany(c => c.Messages).Select(m => m.Text));
        Assert.Equal(2, match.Debriefs.Count());
        Assert.True(match.RecordsOracle);
        Assert.DoesNotContain("Oracle", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("Best moves", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("optimal", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position won", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_arena_asks_the_oracle_once_per_pending_turn_before_the_turn_is_played()
    {
        var spy = new SpyDefinition(_definition);

        await PlayAsync(spy, Moves("1", "1"), Moves("3"), Heap(5));

        Assert.Equal(["assess:move-1", "check", "apply", "assess:move-2", "check", "apply", "assess:move-3", "check", "apply"], spy.Log);
    }

    [Fact]
    public async Task Old_protocols_without_ratings_still_load_and_have_no_verdicts()
    {
        var match = await PlayAsync(_definition, Moves("1", "1"), Moves("3"), Heap(5));
        var withoutOracle = string.Join('\n', File.ReadAllLines(Path.Combine(match.Directory, ProtocolFiles.Events)).Where(l => !l.Contains("\"kind\":\"oracle\"", StringComparison.Ordinal)));
        await File.WriteAllTextAsync(Path.Combine(match.Directory, ProtocolFiles.Events), withoutOracle + "\n", TestContext.Current.CancellationToken);

        var old = MatchRecord.Load(match.Directory);

        Assert.False(old.RecordsOracle);
        Assert.Empty(old.Verdicts(Seat.A));
        Assert.Equal(3, old.Turns().Count());
    }

    [Fact]
    public void A_fact_that_is_no_rating_cannot_be_read_as_one()
    {
        var other = new FactEvent(DateTimeOffset.UnixEpoch, "move-1", "take", FactAudience.Everyone, Seat.A, "x", TemplateRef.Of("x"), null);

        Assert.Throws<ArgumentException>(() => MoveVerdict.From(other));
    }

    private sealed class SpyDefinition(TakeAwayDefinition inner) : IGameDefinition
    {
        public List<string> Log { get; } = [];

        public string Id => inner.Id;

        public string DisplayName => inner.DisplayName;

        public ITemplateSource Templates => inner.Templates;

        public IGame Create(GameSetup setup) => new SpyGame(inner.Create(setup), Log);

        public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => inner.ExtraQuestions(setup);
    }

    private sealed class SpyGame(IGame inner, List<string> log) : IGame, IGameOracle
    {
        public IReadOnlyList<TurnRequest> PendingTurns => inner.PendingTurns;

        public GameResult? Result => inner.Result;

        public TemplateRef RulesFor(Seat seat) => inner.RulesFor(seat);

        public TemplateRef ViewFor(Seat seat) => inner.ViewFor(seat);

        public MoveCheck Check(TurnRequest turn, string move)
        {
            log.Add("check");
            return inner.Check(turn, move);
        }

        public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
        {
            log.Add("apply");
            return inner.Apply(submissions);
        }

        public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => inner.SampleLegalMove(turn, rng);

        public PositionAssessment Assess(TurnRequest turn)
        {
            log.Add($"assess:{turn.TurnId}");
            return ((IGameOracle)inner).Assess(turn);
        }

        public string ChooseMove(TurnRequest turn, PositionAssessment assessment, SeededRng rng) => ((IGameOracle)inner).ChooseMove(turn, assessment, rng);
    }
}
