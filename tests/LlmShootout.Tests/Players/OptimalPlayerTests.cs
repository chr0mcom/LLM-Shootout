using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Games.SplitOrSteal;
using LlmShootout.Tests.TestGames;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests.Players;

public sealed class OptimalPlayerTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly TakeAwayDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Heap(int stones, bool temptation = false, bool estimate = false, int[]? takes = null)
    {
        var options = new JsonObject { ["stones"] = stones };
        if (temptation)
        {
            options["temptation"] = "pass";
        }

        if (estimate)
        {
            options["estimate"] = "required";
        }

        if (takes is not null)
        {
            options["takes"] = new JsonArray([.. takes.Select(t => (JsonNode?)t)]);
        }

        return options;
    }

    private async Task<MatchRecord> PlayAsync(IGameDefinition definition, IPlayer a, IPlayer b, JsonObject? options, Seat first = Seat.A, bool debrief = false, long seed = 7)
    {
        var run = await _harness.RunWithTokenAsync(definition, a, b, false, new MatchOptions { Debrief = debrief }, seed, options, first, TestContext.Current.CancellationToken);
        return MatchRecord.Load(run.Outcome.Directory);
    }

    [Fact]
    public async Task From_a_won_position_the_optimal_player_never_makes_a_mistake_against_the_random_player_from_either_seat()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var optimalSeat = seed % 2 == 0 ? Seat.A : Seat.B;
            var first = seed % 3 == 0 ? Seat.A : Seat.B;
            IPlayer optimal = new OptimalPlayer(seed);
            IPlayer random = new RandomPlayer(seed + 1000);
            var (a, b) = optimalSeat == Seat.A ? (optimal, random) : (random, optimal);

            var match = await PlayAsync(_definition, a, b, Heap(4 + (seed % 13), temptation: true), first, seed: seed);

            var play = PlayMetrics.For(match, optimalSeat);
            var verdicts = match.Verdicts(optimalSeat).ToArray();
            Assert.Equal(0, play.Strength!.SquanderedWins);
            Assert.Equal(0, play.Resignations + play.RuleViolations + play.ExhaustedTurns);
            Assert.All(verdicts.Where(v => v.Value == PositionValue.Won), v => Assert.True(v.Optimal));
            Assert.True(match.Result is not null);
            if (verdicts[0].Value == PositionValue.Won)
            {
                Assert.Equal(1, match.Result!.Seats[optimalSeat].Score);
            }
        }
    }

    [Fact]
    public async Task In_a_lost_position_the_optimal_player_plays_the_move_the_game_documents()
    {
        var match = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(8));

        var first = match.Turns(Seat.A).First();
        var verdict = match.Verdicts(Seat.A).First();

        Assert.Equal(PositionValue.Lost, verdict.Value);
        Assert.Equal("1", first.Move);
        Assert.Equal("b_wins", match.Result!.Outcome);
    }

    [Fact]
    public void A_tie_between_best_moves_is_decided_by_the_generator_and_can_be_repeated()
    {
        var game = _definition.Create(new GameSetup(1, false, "en", Seat.A, Heap(3, takes: [1, 3, 4])));
        var oracle = (IGameOracle)game;
        var turn = game.PendingTurns[0];
        var assessment = oracle.Assess(turn);

        string Choose(long seed) => oracle.ChooseMove(turn, assessment, new SeededRng(seed));
        var choices = Enumerable.Range(0, 40).Select(seed => Choose(seed)).ToArray();

        Assert.Equal(["1", "3"], assessment.BestMoves);
        Assert.Equal(choices, Enumerable.Range(0, 40).Select(seed => Choose(seed)));
        Assert.Equal(["1", "3"], choices.Distinct().Order());
    }

    [Fact]
    public async Task Two_optimal_players_with_ties_repeat_the_same_match_for_the_same_seeds_and_differ_for_other_seeds()
    {
        var options = Heap(31, takes: [1, 3, 4]);

        var first = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), options);
        var again = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), options);
        var others = new List<string>();
        for (var seed = 10; seed < 30; seed++)
        {
            others.Add(Moves(await PlayAsync(_definition, new OptimalPlayer(seed), new OptimalPlayer(seed + 1), options)));
        }

        Assert.Equal(Moves(first), Moves(again));
        Assert.Contains(others, moves => moves != Moves(first));
    }

    private static string Moves(MatchRecord match) => string.Join(',', match.Turns().Select(t => t.Move));

    [Fact]
    public async Task The_optimal_player_costs_nothing_and_makes_no_model_call()
    {
        var match = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(9), debrief: true);

        Assert.Equal(0m, match.CostUsd);
        Assert.Equal(0m, _harness.Cost.TotalUsd);
        Assert.All(match.Calls, call => Assert.Null(call.Call));
        Assert.Equal(0, match.CostUnknownCalls);
    }

    [Fact]
    public async Task The_optimal_player_never_resigns_and_never_passes_even_when_it_is_lost()
    {
        var match = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(8, temptation: true));

        Assert.DoesNotContain(match.Turns(), t => t.Move is "resign" or "pass");
        Assert.DoesNotContain(match.Turns(), t => t.MoveKind is not null);
        Assert.Equal(0, PlayMetrics.For(match, Seat.A).Resignations + PlayMetrics.For(match, Seat.B).Resignations);
    }

    [Fact]
    public async Task The_thought_is_a_short_neutral_text_in_the_language_of_the_match()
    {
        var english = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(5));
        var german = MatchRecord.Load((await _harness.RunWithTokenAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), false, new MatchOptions { Debrief = false }, 7, Heap(5), Seat.A, TestContext.Current.CancellationToken, "de")).Outcome.Directory);

        Assert.All(english.Turns(), t => Assert.Equal("Best move.", t.Thought));
        Assert.All(german.Turns(), t => Assert.Equal("Bester Zug.", t.Thought));
    }

    [Fact]
    public async Task The_estimate_is_100_in_a_won_position_0_in_a_lost_one_and_50_in_a_drawn_one()
    {
        var wonAndLost = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(6, estimate: true));
        var drawn = new OracleOverrideDefinition(turn => turn.TurnId == "move-1" ? new PositionAssessment(PositionValue.Drawn, ["1", "2"]) : new PositionAssessment(PositionValue.Unknown, []));
        var drawnMatch = await PlayAsync(drawn, new OptimalPlayer(1), new OptimalPlayer(2), Heap(6, estimate: true));

        Assert.Equal([(PositionValue.Won, 100), (PositionValue.Lost, 0)], wonAndLost.Verdicts(Seat.A).Concat(wonAndLost.Verdicts(Seat.B)).OrderBy(v => v.TurnId).Take(2).Select(v => (v.Value, v.Estimate!.Value)));
        Assert.Equal([50, 50], drawnMatch.Turns().Take(2).Select(t => t.Estimate!.Value));
    }

    [Fact]
    public async Task The_best_answer_is_offered_only_for_a_turn_that_asks_for_a_move_in_a_game_with_an_oracle()
    {
        var withOracle = new ScriptedPlayer();
        await PlayAsync(_definition, withOracle, new OptimalPlayer(2), Heap(5, estimate: true), debrief: true);
        var withoutOracle = new ScriptedPlayer();
        await PlayAsync(new NumberGuessDefinition(), withoutOracle, new RandomPlayer(2), null);

        var turn = withOracle.Calls[0];
        var answer = JsonNode.Parse(turn.BestAnswer!(new SeededRng(1)))!.AsObject();
        Assert.Equal(("1", "Best move.", 100), (answer["move"]!.GetValue<string>(), answer["thought"]!.GetValue<string>(), answer["estimate"]!.GetValue<int>()));
        Assert.Null(answer["message"]);
        Assert.All(withoutOracle.Calls, call => Assert.Null(call.BestAnswer));
    }

    [Fact]
    public async Task The_debriefing_of_the_optimal_player_is_deterministic_and_consistent_with_its_behavior()
    {
        var match = await PlayAsync(_definition, new OptimalPlayer(1), new OptimalPlayer(2), Heap(9, temptation: true), debrief: true);

        var answers = match.Debriefs.Select(d => d.Answers).ToArray();

        Assert.All(match.Debriefs, d => Assert.True(d.Valid));
        Assert.All(answers, a => Assert.Equal("true", a["rules_followed"]));
        Assert.Equal(answers[0], answers[1]);
    }

    [Fact]
    public async Task The_factory_creates_the_optimal_player_without_a_model_and_it_alone_needs_an_oracle()
    {
        var factory = new PlayerFactory(new EndpointOptions(), ModelCatalog.ForEndpoint(new EndpointOptions(), new FakeTimeProvider()), new FakeTimeProvider());

        var optimal = await factory.CreateAsync("optimal", 3, TestContext.Current.CancellationToken);
        var random = await factory.CreateAsync("random", 3, TestContext.Current.CancellationToken);

        Assert.Equal(("optimal", null, true), (optimal.Name, optimal.Model, optimal.RequiresOracle));
        Assert.False(random.RequiresOracle);
        Assert.False(((IPlayer)new ScriptedPlayer()).RequiresOracle);
    }

    [Fact]
    public async Task A_match_with_the_optimal_player_is_refused_when_the_game_cannot_name_the_best_move_and_no_directory_appears()
    {
        var plan = Plan(new SplitOrStealDefinition(), new OptimalPlayer(1), new RandomPlayer(2), Path.Combine(_harness.Directory, "refused"));

        var failure = await Assert.ThrowsAsync<GameOptionsException>(() => new MatchRunner(_harness.Cost, _harness.Time).RunAsync(plan, TestContext.Current.CancellationToken));

        Assert.Equal("Player 'optimal' needs a game that can name the best move; 'split-or-steal' cannot.", failure.Message);
        Assert.False(Directory.Exists(plan.Directory));
        Assert.False(Directory.Exists(_harness.Directory) && Directory.EnumerateFileSystemEntries(_harness.Directory).Any());
    }

    private static MatchPlan Plan(IGameDefinition definition, IPlayer a, IPlayer b, string directory) => new(
        definition,
        new GameSetup(7, false, "en", Seat.A),
        new Dictionary<Seat, IPlayer> { [Seat.A] = a, [Seat.B] = b },
        new MatchOptions { Debrief = false },
        directory);

    [Fact]
    public async Task A_series_with_the_optimal_player_is_refused_before_the_first_match_is_played()
    {
        Directory.CreateDirectory(_harness.Directory);
        var path = Path.Combine(_harness.Directory, "series.json");
        await File.WriteAllTextAsync(path, """{ "game": "split-or-steal", "seed": 5, "matches": [ {}, {} ] }""", TestContext.Current.CancellationToken);
        var watcher = new ScriptedPlayer();
        var directory = Path.Combine(_harness.Directory, "series");
        var plan = new SeriesPlan(new SplitOrStealDefinition(), SeriesFile.Load(path), watcher, new OptimalPlayer(2), "en", new MatchOptions { Debrief = false }, directory);

        var failure = await Assert.ThrowsAsync<GameOptionsException>(() => new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken));

        Assert.Contains("Player 'optimal' needs a game that can name the best move; 'split-or-steal' cannot.", failure.Message, StringComparison.Ordinal);
        Assert.Empty(watcher.Calls);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task In_a_series_the_optimal_player_is_created_anew_for_every_match()
    {
        Directory.CreateDirectory(_harness.Directory);
        var path = Path.Combine(_harness.Directory, "series.json");
        await File.WriteAllTextAsync(path, """{ "game": "take-away", "seed": 5, "matches": [ { "firstSeat": "A", "options": { "stones": 31, "takes": [1, 3, 4] } }, { "firstSeat": "B", "options": { "stones": 31, "takes": [1, 3, 4] } } ] }""", TestContext.Current.CancellationToken);
        var file = SeriesFile.Load(path);
        var directory = Path.Combine(_harness.Directory, "series");
        var plan = new SeriesPlan(_definition, file, new OptimalPlayer(1), new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, directory);
        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        var seed = file.Seed + 2;
        var alone = Plan(_definition, new OptimalPlayer(seed + SeriesRunner.RandomSeedOffsetA), new RandomPlayer(seed + SeriesRunner.RandomSeedOffsetB), Path.Combine(_harness.Directory, "alone")) with
        {
            Setup = new GameSetup(seed, false, "en", Seat.B, file.Matches[1].Options),
        };
        await new MatchRunner(_harness.Cost, _harness.Time).RunAsync(alone, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.Completed);
        var inSeries = await File.ReadAllTextAsync(Path.Combine(directory, summary.Entries[1].Directory, "events.jsonl"), TestContext.Current.CancellationToken);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(alone.Directory, "events.jsonl"), TestContext.Current.CancellationToken), inSeries);
    }

    [Fact]
    public void The_preflight_lets_every_other_player_through_and_every_oracle_game()
    {
        var oracleGame = _definition.Create(new GameSetup(1, false, "en"));
        var plainGame = new SplitOrStealDefinition().Create(new GameSetup(1, false, "en"));

        MatchPreflight.Ensure(Plan(_definition, new OptimalPlayer(1), new OptimalPlayer(2), "x"), oracleGame);
        MatchPreflight.Ensure(Plan(new SplitOrStealDefinition(), new RandomPlayer(1), new ScriptedPlayer(), "x"), plainGame);

        Assert.Throws<GameOptionsException>(() => MatchPreflight.Ensure(Plan(new SplitOrStealDefinition(), new RandomPlayer(1), new OptimalPlayer(2), "x"), plainGame));
    }
}
