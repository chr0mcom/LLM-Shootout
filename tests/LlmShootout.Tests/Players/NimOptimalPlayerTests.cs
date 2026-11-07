using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Reports;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Players;

public sealed class NimOptimalPlayerTests : IDisposable
{
    private const int Seeds = 500;
    private readonly MatchHarness _harness = new();
    private readonly NimDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private sealed record Variant(bool Misere, int? MaxTake, bool Temptation, bool Resign)
    {
        public JsonObject Options(IEnumerable<int> heaps)
        {
            var options = new JsonObject
            {
                ["heaps"] = new JsonArray([.. heaps.Select(h => (JsonNode?)h)]),
                ["misere"] = Misere,
                ["temptation"] = Temptation ? "pass" : "none",
                ["allowResign"] = Resign,
            };
            if (MaxTake is { } limit)
            {
                options["maxTake"] = limit;
            }

            return options;
        }
    }

    private static readonly Variant[] Variants =
    [
        .. from misere in new[] { false, true }
           from limit in new int?[] { null, 2, 3 }
           from temptation in new[] { false, true }
           select new Variant(misere, limit, temptation, temptation),
    ];

    private async Task<MatchRecord> PlayAsync(IPlayer a, IPlayer b, JsonObject options, Seat first, long seed, bool debrief = false)
    {
        var run = await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = debrief }, seed, options, first, TestContext.Current.CancellationToken);
        return MatchRecord.Load(run.Outcome.Directory);
    }

    private static (IPlayer A, IPlayer B) Players(Seat optimalSeat, long seed)
    {
        IPlayer optimal = new OptimalPlayer(seed);
        IPlayer random = new RandomPlayer(seed + 1000);
        return optimalSeat == Seat.A ? (optimal, random) : (random, optimal);
    }

    private static int[] StartingHeaps(SeededRng rng) => [.. Enumerable.Range(0, 1 + rng.NextInt(4)).Select(_ => 1 + rng.NextInt(7))];

    private static IEnumerable<(string TurnId, int[] HeapsBefore, string Move)> Replay(MatchRecord match)
    {
        var heaps = match.Facts.First(f => f.Kind == "start").Data!["heaps"].Split(',').Select(int.Parse).ToArray();
        foreach (var turn in match.Turns().Where(t => t.MoveKind is null or MoveKind.Regular))
        {
            yield return (turn.TurnId, [.. heaps], turn.Move!);
            var move = NimMove.Parse(turn.Move!);
            heaps[move.Heap - 1] -= move.Count;
        }
    }

    [Fact]
    public async Task From_a_won_position_the_optimal_player_never_loses_to_the_random_player_in_any_variant_from_either_seat()
    {
        var wonGames = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var variant = Variants[seed % Variants.Length];
            var heaps = StartingHeaps(new SeededRng(seed));
            var optimalSeat = seed % 2 == 0 ? Seat.A : Seat.B;
            var first = seed % 4 < 2 ? Seat.A : Seat.B;
            var (a, b) = Players(optimalSeat, seed);

            var match = await PlayAsync(a, b, variant.Options(heaps), first, seed);

            var label = $"seed {seed}, {variant}, heaps [{string.Join(',', heaps)}], optimal {optimalSeat}, first {first}";
            var verdicts = match.Verdicts(optimalSeat).ToArray();
            Assert.True(match.Result is not null, label);
            Assert.True(PlayMetrics.For(match, optimalSeat).Strength!.SquanderedWins == 0, label);
            if (verdicts.Any(v => v.Value == PositionValue.Won))
            {
                wonGames++;
                Assert.True(match.Result!.Seats[optimalSeat].Score == 1, label);
            }
        }

        Assert.True(wonGames > Seeds / 2, $"only {wonGames} of {Seeds} games had a won position for the optimal player");
    }

    [Fact]
    public async Task In_a_lost_position_the_optimal_player_plays_one_stone_from_the_largest_heap_and_from_the_smallest_number_on_a_tie()
    {
        var lostMoves = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var variant = Variants[seed % Variants.Length];
            var optimalSeat = seed % 2 == 0 ? Seat.A : Seat.B;
            var (a, b) = Players(optimalSeat, seed);

            var match = await PlayAsync(a, b, variant.Options(StartingHeaps(new SeededRng(seed))), Seat.A, seed);

            var verdicts = match.Verdicts(optimalSeat).Where(v => v.Value == PositionValue.Lost).Select(v => v.TurnId).ToHashSet();
            foreach (var (turnId, heaps, move) in Replay(match).Where(r => verdicts.Contains(r.TurnId)))
            {
                Assert.Equal(NimOracle.LosingMove(heaps).ToString(), move);
                Assert.Equal(heaps.Max(), heaps[NimMove.Parse(move).Heap - 1]);
                Assert.Equal(heaps.ToList().IndexOf(heaps.Max()) + 1, NimMove.Parse(move).Heap);
                Assert.Equal(1, NimMove.Parse(move).Count);
                lostMoves++;
            }
        }

        Assert.True(lostMoves > 100, $"only {lostMoves} moves in a lost position were played");
    }

    [Fact]
    public async Task The_optimal_player_never_passes_resigns_or_gives_up_even_in_a_lost_position_with_every_temptation()
    {
        var options = new Variant(false, null, true, true).Options([1, 4, 5]);

        foreach (var seed in Enumerable.Range(1, 40))
        {
            var match = await PlayAsync(new OptimalPlayer(seed), new RandomPlayer(seed), options, Seat.A, seed);

            var play = PlayMetrics.For(match, Seat.A);
            Assert.Equal((0, 0, 0), (play.RuleViolations, play.Resignations, play.ExhaustedTurns));
            Assert.All(match.Turns(Seat.A), turn => Assert.Equal(("played", null), (turn.Status, turn.MoveKind)));
            Assert.DoesNotContain(match.Facts, f => f.Kind is "pass" or "resign" or "forfeit");
        }
    }

    [Fact]
    public async Task The_optimal_player_estimates_one_hundred_in_a_won_position_and_zero_in_a_lost_one()
    {
        var match = await PlayAsync(new OptimalPlayer(1), new RandomPlayer(2), new Variant(false, null, false, false).Options([3, 4, 5]), Seat.A, 3);

        var verdicts = match.Verdicts(Seat.A).ToArray();

        Assert.Equal(PositionValue.Won, verdicts[0].Value);
        Assert.All(verdicts, v => Assert.Equal(v.Value == PositionValue.Won ? 100 : 0, v.Estimate));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Two_optimal_players_play_every_variant_to_the_end_and_the_first_mover_wins_exactly_if_the_start_is_won(bool temptation, bool resign)
    {
        foreach (var variant in Variants.Where(v => v.Temptation == temptation && v.Resign == resign))
        {
            foreach (var heaps in new[] { new[] { 3, 4, 5 }, [1, 4, 5], [7], [2, 2], [1, 1, 1, 1, 1] })
            {
                var match = await PlayAsync(new OptimalPlayer(1), new OptimalPlayer(2), variant.Options(heaps), Seat.A, 4, debrief: true);

                var won = NimOracle.For(variant.Misere, variant.MaxTake).Wins(heaps);
                Assert.True(match.Result is not null, $"{variant}, [{string.Join(',', heaps)}]");
                Assert.Equal(won ? "a_wins" : "b_wins", match.Result!.Outcome);
                Assert.Equal("last_stone", match.Result.Metrics![NimGame.EndMetric]);
                Assert.All(match.Debriefs, debrief => Assert.True(debrief.Valid));
                Assert.Equal(0, match.Turns().Count(t => t.MoveKind is not null and not MoveKind.Regular));
            }
        }
    }

    [Fact]
    public async Task The_same_seeds_repeat_a_match_with_the_optimal_player_move_for_move()
    {
        var options = new Variant(false, null, false, false).Options([5, 5, 5]);

        var first = await PlayAsync(new OptimalPlayer(9), new OptimalPlayer(10), options, Seat.A, 11);
        var again = await PlayAsync(new OptimalPlayer(9), new OptimalPlayer(10), options, Seat.A, 11);
        var others = new List<string>();
        foreach (var seed in Enumerable.Range(20, 20))
        {
            others.Add(Moves(await PlayAsync(new OptimalPlayer(seed), new OptimalPlayer(seed + 1), options, Seat.A, 11)));
        }

        Assert.Equal(Moves(first), Moves(again));
        Assert.Contains(others, moves => moves != Moves(first));
    }

    private static string Moves(MatchRecord match) => string.Join(' ', match.Turns().Select(t => t.Move));
}
