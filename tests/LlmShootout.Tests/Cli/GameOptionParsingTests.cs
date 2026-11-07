using System.Text.Json;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Cli;
using LlmShootout.Cli.Commands;
using LlmShootout.Games.SplitOrSteal;

namespace LlmShootout.Tests.Cli;

public sealed class GameOptionParsingTests
{
    private static JsonNode? Value(string pair) => RunOptionSet.ParseGameOptions([pair])!["key"];

    [Theory]
    [InlineData("key=1000", JsonValueKind.Number)]
    [InlineData("key=-3", JsonValueKind.Number)]
    [InlineData("key=1.5", JsonValueKind.Number)]
    [InlineData("key=true", JsonValueKind.True)]
    [InlineData("key=false", JsonValueKind.False)]
    [InlineData("key=pass", JsonValueKind.String)]
    [InlineData("key=\"1\"", JsonValueKind.String)]
    [InlineData("key=[1,4,5]", JsonValueKind.Array)]
    [InlineData("key={\"count\":3,\"max\":9}", JsonValueKind.Object)]
    public void A_value_is_read_as_json(string pair, JsonValueKind expected)
    {
        Assert.Equal(expected, Value(pair)!.GetValueKind());
    }

    [Fact]
    public void Numbers_arrays_and_quoted_text_keep_their_content()
    {
        var options = RunOptionSet.ParseGameOptions(["pot=1000", "heaps=[1,4,5]", "x=\"1\"", "temptation=pass", "misere=true"])!;

        Assert.Equal(1000, options["pot"]!.GetValue<int>());
        Assert.Equal([1, 4, 5], options["heaps"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal("1", options["x"]!.GetValue<string>());
        Assert.Equal("pass", options["temptation"]!.GetValue<string>());
        Assert.True(options["misere"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("key=007", "007")]
    [InlineData("key=+5", "+5")]
    [InlineData("key=True", "True")]
    [InlineData("key=[1,", "[1,")]
    [InlineData("key=two words", "two words")]
    [InlineData("key=", "")]
    [InlineData("key=a=b", "a=b")]
    public void A_value_that_is_not_json_is_text(string pair, string expected)
    {
        Assert.Equal(expected, Value(pair)!.GetValue<string>());
    }

    [Fact]
    public void The_json_literal_null_is_a_null_value()
    {
        var options = RunOptionSet.ParseGameOptions(["key=null"])!;

        Assert.True(options.ContainsKey("key"));
        Assert.Null(options["key"]);
    }

    [Fact]
    public void Keys_and_values_are_trimmed()
    {
        Assert.Equal(7, RunOptionSet.ParseGameOptions([" key = 7 "])!["key"]!.GetValue<int>());
    }

    [Fact]
    public void A_key_that_is_given_twice_is_a_usage_error()
    {
        var ex = Assert.Throws<UsageException>(() => RunOptionSet.ParseGameOptions(["pot=10", "pot=20"]));

        Assert.Contains("'pot' is given more than once", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pot")]
    [InlineData("=5")]
    public void A_pair_without_a_key_or_a_separator_is_a_usage_error(string pair)
    {
        Assert.Throws<UsageException>(() => RunOptionSet.ParseGameOptions([pair]));
    }

    [Fact]
    public void Without_pairs_there_are_no_options()
    {
        Assert.Null(RunOptionSet.ParseGameOptions(null));
        Assert.Null(RunOptionSet.ParseGameOptions([]));
    }

    [Fact]
    public void Options_of_split_or_steal_from_the_command_line_are_still_accepted()
    {
        var setup = new GameSetup(1, true, "en", Seat.A, RunOptionSet.ParseGameOptions(["pot=1000", "messagesPerPlayer=5"]));

        var options = SplitOrStealOptions.From(setup.Options);

        Assert.Equal(new SplitOrStealOptions(1000, 5), options);
    }

    [Theory]
    [InlineData("pot=1.5")]
    [InlineData("pot=\"100\"")]
    [InlineData("pot=many")]
    public void A_value_of_the_wrong_type_is_reported_by_the_game(string pair)
    {
        var json = RunOptionSet.ParseGameOptions([pair]);

        var ex = Assert.Throws<GameOptionsException>(() => SplitOrStealOptions.From(json));

        Assert.Contains("'pot' must be an integer", ex.Message, StringComparison.Ordinal);
    }
}
