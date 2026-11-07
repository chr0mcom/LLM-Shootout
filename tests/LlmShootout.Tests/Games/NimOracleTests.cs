using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Games;

public sealed class NimOracleTests
{
    private const int NoLimit = int.MaxValue;

    private static readonly NimDefinition Definition = new();

    private static NimGame Create(string options, long seed = 1) =>
        (NimGame)Definition.Create(new GameSetup(seed, false, "en", Seat.A, JsonNode.Parse(options)!.AsObject()));

    /// <summary>Every ordered position of 1 to <paramref name="maxHeaps"/> heaps with 0 to <paramref name="maxStones"/> stones each.</summary>
    private static IEnumerable<int[]> Positions(int maxHeaps, int maxStones)
    {
        for (var count = 1; count <= maxHeaps; count++)
        {
            var heaps = new int[count];
            do
            {
                yield return [.. heaps];
            }
            while (Next(heaps, maxStones));
        }
    }

    private static bool Next(int[] heaps, int maxStones)
    {
        for (var i = 0; i < heaps.Length; i++)
        {
            if (heaps[i] < maxStones)
            {
                heaps[i]++;
                return true;
            }

            heaps[i] = 0;
        }

        return false;
    }

    private static int Xor(IEnumerable<int> values) => values.Aggregate(0, (a, b) => a ^ b);

    [Fact]
    public void In_normal_play_without_a_limit_a_position_is_lost_exactly_when_the_xor_of_the_heaps_is_zero()
    {
        var oracle = NimOracle.For(false, null);
        var checkedPositions = 0;

        foreach (var heaps in Positions(5, 7))
        {
            Assert.True(oracle.Wins(heaps) == (Xor(heaps) != 0), $"[{string.Join(',', heaps)}]");
            checkedPositions++;
        }

        Assert.Equal(8 + 64 + 512 + 4096 + 32768, checkedPositions);
    }

    [Fact]
    public void In_misere_without_a_limit_the_mover_loses_with_an_odd_number_of_heaps_of_one_if_no_heap_is_larger_and_else_at_xor_zero()
    {
        var oracle = NimOracle.For(true, null);

        foreach (var heaps in Positions(5, 7))
        {
            var onlySingles = heaps.All(stones => stones <= 1);
            var expectedWin = onlySingles ? heaps.Count(stones => stones == 1) % 2 == 0 : Xor(heaps) != 0;
            Assert.True(oracle.Wins(heaps) == expectedWin, $"[{string.Join(',', heaps)}]");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(9)]
    public void In_normal_play_with_a_limit_k_the_grundy_value_of_a_heap_is_n_mod_k_plus_one_and_a_position_is_lost_at_xor_zero(int limit)
    {
        var oracle = NimOracle.For(false, limit);

        foreach (var heaps in Positions(4, 9))
        {
            var grundy = Xor(heaps.Select(stones => stones % (limit + 1)));
            Assert.True(oracle.Wins(heaps) == (grundy != 0), $"limit {limit}, [{string.Join(',', heaps)}]");
        }
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    public void In_every_small_position_the_oracle_with_a_limit_agrees_with_a_brute_force_search(bool misere, int limit)
    {
        var oracle = NimOracle.For(misere, limit);

        foreach (var heaps in Positions(3, 5))
        {
            Assert.True(
                oracle.Wins(heaps) == NimBruteForce.MoverWins([.. heaps], misere, limit),
                $"misere {misere}, limit {limit}, [{string.Join(',', heaps)}]");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_a_limit_the_oracle_agrees_with_the_brute_force_search_in_both_variants(bool misere)
    {
        var oracle = NimOracle.For(misere, null);

        foreach (var heaps in Positions(3, 5))
        {
            Assert.True(oracle.Wins(heaps) == NimBruteForce.MoverWins([.. heaps], misere, NoLimit), $"misere {misere}, [{string.Join(',', heaps)}]");
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    public void The_winning_moves_are_the_brute_force_set_and_use_the_real_heap_numbers(bool misere, int? limit)
    {
        var oracle = NimOracle.For(misere, limit);

        foreach (var heaps in Positions(3, 5).Where(h => h.Sum() > 0))
        {
            var expected = NimBruteForce.WinningMoves([.. heaps], misere, limit ?? NoLimit);
            Assert.Equal(expected, oracle.WinningMoves(heaps).Select(move => move.ToString()));
        }
    }

    [Fact]
    public void The_winning_moves_of_equal_heaps_name_every_heap_by_its_own_number()
    {
        var game = Create("""{ "heaps": [3, 3, 1] }""");

        var assessment = game.Assess(game.PendingTurns.Single());

        Assert.Equal(PositionValue.Won, assessment.Value);
        Assert.Equal(["1:1", "2:1", "3:1"], assessment.BestMoves);
    }

    [Fact]
    public void A_lost_position_has_no_best_moves_and_a_won_one_names_all_of_them_sorted_by_heap_and_count()
    {
        var lost = Create("""{ "heaps": [1, 4, 5] }""");
        var won = Create("""{ "heaps": [3, 4, 5] }""");
        var many = Create("""{ "heaps": [5, 5, 5] }""");

        var lostAssessment = lost.Assess(lost.PendingTurns.Single());

        Assert.Equal(PositionValue.Lost, lostAssessment.Value);
        Assert.Empty(lostAssessment.BestMoves);
        Assert.Equal(["1:2"], won.Assess(won.PendingTurns.Single()).BestMoves);
        Assert.Equal(["1:5", "2:5", "3:5"], many.Assess(many.PendingTurns.Single()).BestMoves);
    }

    [Fact]
    public void The_positions_of_misere_and_of_a_limit_are_rated_with_their_own_rules()
    {
        var misere = Create("""{ "heaps": [1, 1], "misere": true }""");
        var singleHeapMisere = Create("""{ "heaps": [1], "misere": true }""");
        var limited = Create("""{ "heaps": [4], "maxTake": 3 }""");

        Assert.Equal(PositionValue.Won, misere.Assess(misere.PendingTurns.Single()).Value);
        Assert.Equal(PositionValue.Lost, singleHeapMisere.Assess(singleHeapMisere.PendingTurns.Single()).Value);
        Assert.Equal(PositionValue.Lost, limited.Assess(limited.PendingTurns.Single()).Value);
    }

    [Fact]
    public void In_misere_with_heaps_of_one_and_a_larger_heap_the_winning_move_leaves_an_odd_number_of_singles()
    {
        var game = Create("""{ "heaps": [1, 2], "misere": true }""");

        Assert.Equal(["2:2"], game.Assess(game.PendingTurns.Single()).BestMoves);
    }

    [Theory]
    [InlineData("[1, 4, 5]", "3:1")]
    [InlineData("[5, 5]", "1:1")]
    [InlineData("[2, 7, 7, 2]", "2:1")]
    [InlineData("[1, 2, 3]", "3:1")]
    public void In_a_lost_position_the_choice_is_one_stone_from_the_largest_heap_and_from_the_smallest_number_on_a_tie(string heaps, string expected)
    {
        var game = Create($$"""{ "heaps": {{heaps}} }""");
        var turn = game.PendingTurns.Single();
        var assessment = game.Assess(turn);

        Assert.Equal(PositionValue.Lost, assessment.Value);
        Assert.Equal(expected, game.ChooseMove(turn, assessment, new SeededRng(5)));
    }

    [Fact]
    public void The_choice_in_a_lost_position_does_not_depend_on_the_generator_and_is_always_a_regular_move()
    {
        var game = Create("""{ "heaps": [1, 4, 5], "maxTake": 1, "temptation": "pass", "allowResign": true }""");
        var turn = game.PendingTurns.Single();
        var assessment = game.Assess(turn);

        var choices = Enumerable.Range(0, 30).Select(seed => game.ChooseMove(turn, assessment, new SeededRng(seed))).Distinct().ToArray();

        Assert.Equal(PositionValue.Lost, assessment.Value);
        Assert.Equal(["3:1"], choices);
        Assert.True(game.Check(turn, choices[0]).IsValid);
        Assert.Equal(MoveKind.Regular, game.Check(turn, choices[0]).Kind);
    }

    [Fact]
    public void In_a_won_position_the_choice_is_one_of_the_best_moves_decided_by_the_generator_and_repeatable()
    {
        var game = Create("""{ "heaps": [3, 3, 1] }""");
        var turn = game.PendingTurns.Single();
        var assessment = game.Assess(turn);

        string Choose(long seed) => game.ChooseMove(turn, assessment, new SeededRng(seed));
        var choices = Enumerable.Range(0, 60).Select(seed => Choose(seed)).ToArray();

        Assert.Equal(choices, Enumerable.Range(0, 60).Select(seed => Choose(seed)));
        Assert.Equal(assessment.BestMoves.Order(), choices.Distinct().Order());
    }

    [Fact]
    public void The_largest_position_of_the_game_is_solved_and_the_oracles_of_a_variant_are_shared()
    {
        var oracle = NimOracle.For(false, null);

        Assert.True(oracle.Wins([15, 15, 15, 15, 15]));
        Assert.False(oracle.Wins([15, 15, 15, 15]));
        Assert.Same(oracle, NimOracle.For(false, null));
        Assert.NotSame(oracle, NimOracle.For(true, null));
        Assert.NotSame(oracle, NimOracle.For(false, 7));
    }
}
