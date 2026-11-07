using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Games;

public sealed class NimOptionsTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    private static NimOptions From(string text, long seed = 1) => NimOptions.From(Json(text), seed);

    [Fact]
    public void Without_options_the_game_is_normal_play_on_three_four_five_with_estimates()
    {
        var options = NimOptions.From(null, 1);

        Assert.Equal([3, 4, 5], options.Heaps);
        Assert.Equal((false, null, false, false, true), (options.Misere, options.MaxTake, options.PassTemptation, options.AllowResign, options.Estimate));
        Assert.Equal(25, options.MaxMoves);
        Assert.Equal(options, NimOptions.From(new JsonObject(), 99));
    }

    [Fact]
    public void Every_option_can_be_set()
    {
        var options = From("""
            { "heaps": [1, 4, 5], "misere": true, "maxTake": 2, "temptation": "pass", "allowResign": true, "estimate": false, "maxMoves": 40 }
            """);

        Assert.Equal([1, 4, 5], options.Heaps);
        Assert.Equal((true, 2, true, true, false, 40), (options.Misere, options.MaxTake, options.PassTemptation, options.AllowResign, options.Estimate, options.MaxMoves));
    }

    [Fact]
    public void The_default_move_limit_is_twice_the_stones_plus_one_and_scales_with_the_heaps()
    {
        Assert.Equal(21, From("""{ "heaps": [10] }""").MaxMoves);
        Assert.Equal(151, From("""{ "heaps": [15, 15, 15, 15, 15] }""").MaxMoves);
        Assert.Equal(3, From("""{ "heaps": [1] }""").MaxMoves);
    }

    [Theory]
    [InlineData("""{ "heaps": [1] }""", 1)]
    [InlineData("""{ "heaps": [15, 15, 15, 15, 15] }""", 5)]
    public void The_smallest_and_the_largest_position_are_allowed(string json, int heaps)
    {
        Assert.Equal(heaps, From(json).Heaps.Count);
    }

    [Fact]
    public void A_none_temptation_and_null_values_mean_the_defaults()
    {
        var options = From("""{ "temptation": "none", "maxTake": null, "misere": null }""");

        Assert.Equal((false, null, false), (options.PassTemptation, options.MaxTake, options.Misere));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(2027)]
    public void Random_heaps_are_drawn_from_the_seed_within_the_bounds(long seed)
    {
        var options = From("""{ "randomHeaps": { "count": 4, "max": 6 } }""", seed);

        Assert.Equal(4, options.Heaps.Count);
        Assert.All(options.Heaps, stones => Assert.InRange(stones, 1, 6));
        Assert.Equal(options.Heaps, From("""{ "randomHeaps": { "count": 4, "max": 6 } }""", seed).Heaps);
    }

    [Fact]
    public void Random_heaps_differ_between_seeds_and_use_the_whole_range()
    {
        var drawn = Enumerable.Range(1, 60).Select(seed => From("""{ "randomHeaps": { "count": 5, "max": 15 } }""", seed).Heaps).ToArray();

        Assert.True(drawn.Select(h => string.Join(',', h)).Distinct().Count() > 30);
        Assert.Equal(1, drawn.SelectMany(h => h).Min());
        Assert.Equal(15, drawn.SelectMany(h => h).Max());
        Assert.All(Enumerable.Range(1, 20), seed => Assert.Equal([1, 1], From("""{ "randomHeaps": { "count": 2, "max": 1 } }""", seed).Heaps));
    }

    [Fact]
    public void The_move_limit_default_follows_the_drawn_heaps()
    {
        var options = From("""{ "randomHeaps": { "count": 3, "max": 9 } }""", 5);

        Assert.Equal(2 * options.Heaps.Sum() + 1, options.MaxMoves);
    }

    [Fact]
    public void Unknown_options_are_named_together_with_the_known_ones()
    {
        var ex = Assert.Throws<GameOptionsException>(() => From("""{ "heap": [1], "Misere": true, "misere": false }"""));

        Assert.Equal("Unknown option(s) for nim: heap, Misere. Allowed: heaps, randomHeaps, misere, maxTake, temptation, allowResign, estimate, maxMoves.", ex.Message);
    }

    [Theory]
    [InlineData("""{ "heaps": [] }""", "must name 1 to 5 heaps, but names 0")]
    [InlineData("""{ "heaps": [1, 1, 1, 1, 1, 1] }""", "must name 1 to 5 heaps, but names 6")]
    [InlineData("""{ "heaps": [3, 0] }""", "Heap 2 of option 'heaps' must be a whole number from 1 to 15, but is 0")]
    [InlineData("""{ "heaps": [16] }""", "Heap 1 of option 'heaps' must be a whole number from 1 to 15, but is 16")]
    [InlineData("""{ "heaps": [-2] }""", "but is -2")]
    [InlineData("""{ "heaps": [3, 2.5] }""", "Heap 2 of option 'heaps' must be a whole number.")]
    [InlineData("""{ "heaps": ["3"] }""", "Heap 1 of option 'heaps' must be a whole number.")]
    [InlineData("""{ "heaps": [3, null] }""", "Heap 2 of option 'heaps' must be a whole number.")]
    [InlineData("""{ "heaps": [true] }""", "Heap 1 of option 'heaps' must be a whole number.")]
    [InlineData("""{ "heaps": "3,4,5" }""", "Option 'heaps' must be an array of whole numbers")]
    [InlineData("""{ "heaps": 4 }""", "Option 'heaps' must be an array of whole numbers")]
    [InlineData("""{ "heaps": [1], "randomHeaps": { "count": 1, "max": 3 } }""", "exclude each other")]
    [InlineData("""{ "randomHeaps": 3 }""", "Option 'randomHeaps' must be an object like")]
    [InlineData("""{ "randomHeaps": { "count": 2, "max": 4, "min": 1 } }""", "has no other keys")]
    [InlineData("""{ "randomHeaps": { "count": 2 } }""", "it needs both 'count' and 'max'")]
    [InlineData("""{ "randomHeaps": { "max": 2 } }""", "it needs both 'count' and 'max'")]
    [InlineData("""{ "randomHeaps": { "count": 0, "max": 4 } }""", "'count' of option 'randomHeaps' must be a whole number from 1 to 5, but is 0")]
    [InlineData("""{ "randomHeaps": { "count": 6, "max": 4 } }""", "'count' of option 'randomHeaps' must be a whole number from 1 to 5, but is 6")]
    [InlineData("""{ "randomHeaps": { "count": 2, "max": 0 } }""", "'max' of option 'randomHeaps' must be a whole number from 1 to 15, but is 0")]
    [InlineData("""{ "randomHeaps": { "count": 2, "max": 16 } }""", "'max' of option 'randomHeaps' must be a whole number from 1 to 15, but is 16")]
    [InlineData("""{ "randomHeaps": { "count": "2", "max": 4 } }""", "'count' of option 'randomHeaps' must be a whole number.")]
    [InlineData("""{ "misere": "yes" }""", "Option 'misere' must be true or false.")]
    [InlineData("""{ "allowResign": 1 }""", "Option 'allowResign' must be true or false.")]
    [InlineData("""{ "estimate": "false" }""", "Option 'estimate' must be true or false.")]
    [InlineData("""{ "maxTake": 0 }""", "Option 'maxTake' must be a whole number from 1 to 15, but is 0")]
    [InlineData("""{ "maxTake": 16 }""", "Option 'maxTake' must be a whole number from 1 to 15, but is 16")]
    [InlineData("""{ "maxTake": 2.5 }""", "Option 'maxTake' must be a whole number.")]
    [InlineData("""{ "maxTake": "3" }""", "Option 'maxTake' must be a whole number.")]
    [InlineData("""{ "temptation": "maybe" }""", "Option 'temptation' must be \"none\" or \"pass\".")]
    [InlineData("""{ "temptation": 3 }""", "Option 'temptation' must be \"none\" or \"pass\".")]
    [InlineData("""{ "temptation": true }""", "Option 'temptation' must be \"none\" or \"pass\".")]
    [InlineData("""{ "maxMoves": 12 }""", "Option 'maxMoves' must be a whole number greater than the 12 stones in play (at most 1000), but is 12")]
    [InlineData("""{ "heaps": [2, 2], "maxMoves": 4 }""", "greater than the 4 stones in play")]
    [InlineData("""{ "maxMoves": 1001 }""", "but is 1001")]
    [InlineData("""{ "maxMoves": "many" }""", "Option 'maxMoves' must be a whole number.")]
    public void Every_invalid_option_is_rejected_with_a_message_that_says_what_is_expected(string json, string expectedPart)
    {
        var ex = Assert.Throws<GameOptionsException>(() => From(json));

        Assert.Contains(expectedPart, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_move_limit_just_above_the_stones_is_the_smallest_allowed()
    {
        Assert.Equal(13, From("""{ "maxMoves": 13 }""").MaxMoves);
        Assert.Equal(1000, From("""{ "maxMoves": 1000 }""").MaxMoves);
    }

    [Fact]
    public void The_definition_creates_the_game_from_its_options_and_reports_them_as_invalid_early()
    {
        var definition = new NimDefinition();

        Assert.Throws<GameOptionsException>(() => definition.Create(new GameSetup(1, false, "en", Seat.A, Json("""{ "misere": 3 }"""))));
        Assert.Throws<ArgumentNullException>(() => definition.Create(null!));
    }
}
