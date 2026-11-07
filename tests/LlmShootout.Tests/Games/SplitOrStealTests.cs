using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Games;
using LlmShootout.Games.SplitOrSteal;

namespace LlmShootout.Tests.Games;

public sealed class SplitOrStealTests
{
    private static readonly SplitOrStealDefinition Definition = new();

    private static IGame Create(bool communication = true, Seat first = Seat.A, JsonObject? options = null) =>
        Definition.Create(new GameSetup(1, communication, "en", first, options));

    private static GameResult Decide(string? a, string? b)
    {
        var game = Create(communication: false);
        TurnSubmission Sub(Seat seat, string? move) => new("decide", seat, move, move is null ? SubmissionStatus.Exhausted : SubmissionStatus.Played);
        game.Apply([Sub(Seat.A, a), Sub(Seat.B, b)]);
        return game.Result!;
    }

    [Theory]
    [InlineData("split", "split", "both_split", 50, 50)]
    [InlineData("steal", "split", "a_stole", 100, 0)]
    [InlineData("split", "steal", "b_stole", 0, 100)]
    [InlineData("steal", "steal", "both_steal", 0, 0)]
    public void Pays_out_every_combination(string a, string b, string outcome, double pa, double pb)
    {
        var result = Decide(a, b);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal((pa, pb), (result.Seats[Seat.A].Score, result.Seats[Seat.B].Score));
        Assert.Equal((a, b), (result.Seats[Seat.A].Label, result.Seats[Seat.B].Label));
    }

    [Theory]
    [InlineData(null, "split", "no_decision_a", 0, 50)]
    [InlineData(null, "steal", "no_decision_a", 0, 100)]
    [InlineData("split", null, "no_decision_b", 50, 0)]
    [InlineData("steal", null, "no_decision_b", 100, 0)]
    [InlineData(null, null, "no_decision_both", 0, 0)]
    public void A_missing_decision_is_its_own_outcome_worth_zero_for_the_failing_seat(string? a, string? b, string outcome, double pa, double pb)
    {
        var result = Decide(a, b);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal((pa, pb), (result.Seats[Seat.A].Score, result.Seats[Seat.B].Score));
        Assert.Contains("no_decision", new[] { result.Seats[Seat.A].Label, result.Seats[Seat.B].Label });
    }

    [Fact]
    public void Talking_phase_alternates_and_only_allows_messages()
    {
        var game = Create(first: Seat.B);
        var order = new List<(Seat, FieldRule, FieldRule)>();
        while (game.PendingTurns is [{ Kind: "talk" } turn])
        {
            order.Add((turn.Seat, turn.Message, turn.Move));
            game.Apply([new TurnSubmission(turn.TurnId, turn.Seat, null, SubmissionStatus.Played)]);
        }

        Assert.Equal(6, order.Count);
        Assert.Equal([Seat.B, Seat.A, Seat.B, Seat.A, Seat.B, Seat.A], order.Select(o => o.Item1));
        Assert.All(order, o => Assert.Equal((FieldRule.Required, FieldRule.Forbidden), (o.Item2, o.Item3)));
    }

    [Fact]
    public void Decision_phase_is_simultaneous_with_a_required_move_and_no_message()
    {
        var game = Create(communication: false);

        var turns = game.PendingTurns;

        Assert.Equal([Seat.A, Seat.B], turns.Select(t => t.Seat));
        Assert.All(turns, t =>
        {
            Assert.Equal(("decide", FieldRule.Required, FieldRule.Forbidden), (t.Kind, t.Move, t.Message));
            Assert.Equal(["split", "steal"], t.Format!.AllowedValues);
        });
    }

    [Fact]
    public void Without_communication_the_game_starts_with_the_decision()
    {
        Assert.Equal("decide", Create(communication: false).PendingTurns[0].TurnId);
    }

    [Fact]
    public void The_messages_per_player_option_changes_the_length_of_the_talk()
    {
        var game = Create(options: new JsonObject { ["messagesPerPlayer"] = 1 });
        var talks = 0;
        while (game.PendingTurns is [{ Kind: "talk" } turn])
        {
            talks++;
            game.Apply([new TurnSubmission(turn.TurnId, turn.Seat, null, SubmissionStatus.Played)]);
        }

        Assert.Equal(2, talks);
    }

    [Theory]
    [InlineData("split", "split")]
    [InlineData(" STEAL ", "steal")]
    public void Check_normalizes_moves(string input, string expected)
    {
        var game = Create(communication: false);

        Assert.Equal(expected, game.Check(game.PendingTurns[0], input).NormalizedMove);
    }

    [Fact]
    public void Check_rejects_anything_else_with_a_stable_code_and_the_allowed_values()
    {
        var game = Create(communication: false);

        var check = game.Check(game.PendingTurns[0], "share");

        Assert.False(check.IsValid);
        Assert.Equal("not-allowed-value", check.ErrorCode);
        Assert.Equal("\"split\", \"steal\"", check.Allowed);
    }

    [Fact]
    public void Reveals_decisions_and_payouts_as_public_facts_after_both_decided()
    {
        var game = Create(communication: false);

        var resolution = game.Apply([new("decide", Seat.A, "split", SubmissionStatus.Played), new("decide", Seat.B, "steal", SubmissionStatus.Played)]);

        Assert.Equal(4, resolution.Facts.Count);
        Assert.All(resolution.Facts, f => Assert.Equal(FactAudience.Everyone, f.Audience));
        Assert.Equal(["split", "steal"], resolution.Facts.Where(f => f.Kind == "decision").Select(f => f.Data!["decision"]));
        Assert.Empty(game.PendingTurns);
    }

    [Fact]
    public void The_view_never_contains_a_decision_before_the_reveal()
    {
        var game = Create(communication: false);

        var text = Definition.Templates.Render(game.ViewFor(Seat.A), "en");

        Assert.DoesNotContain("split", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sampling_yields_legal_moves_only_for_decisions()
    {
        var game = Create();
        var talk = game.PendingTurns[0];

        Assert.Null(game.SampleLegalMove(talk, new SeededRng(1)));
        var decide = Create(communication: false).PendingTurns[0];
        var rng = new SeededRng(1);
        Assert.All(Enumerable.Range(0, 20), _ => Assert.Contains(game.SampleLegalMove(decide, rng), SplitOrStealGame.Moves));
    }

    [Theory]
    [InlineData("{\"pot\":7}", "even number")]
    [InlineData("{\"pot\":0}", "even number")]
    [InlineData("{\"messagesPerPlayer\":11}", "between 0 and 10")]
    [InlineData("{\"potSize\":10}", "Unknown option")]
    [InlineData("{\"pot\":\"ten\"}", "must be an integer")]
    public void Rejects_invalid_options_with_a_helpful_message(string json, string expected)
    {
        var ex = Assert.Throws<GameOptionsException>(() => Create(options: JsonNode.Parse(json)!.AsObject()));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pot_and_rules_follow_the_options_in_both_languages()
    {
        var game = Create(options: new JsonObject { ["pot"] = 1000 });

        Assert.Contains("1000", Definition.Templates.Render(game.RulesFor(Seat.A), "en"), StringComparison.Ordinal);
        Assert.Contains("500", Definition.Templates.Render(game.RulesFor(Seat.A), "de"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalog_registers_the_game_explicitly()
    {
        Assert.Same(GameCatalog.All[0], GameCatalog.Find("SPLIT-OR-STEAL"));
        Assert.Null(GameCatalog.Find("chess"));
        Assert.Empty(Definition.ExtraQuestions(new GameSetup(1, true, "de")));
        Assert.Empty(Definition.ExtraQuestions(new GameSetup(1, false, "de")));
    }
}
