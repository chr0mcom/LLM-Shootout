using LlmShootout.Arena;
using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Templates;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Answers;

public sealed class AnswerEvaluatorTests
{
    private static (AnswerEvaluator Evaluator, IGame Game) Arrange()
    {
        var definition = new NumberGuessDefinition();
        var options = new MatchOptions();
        var prompts = new PromptBuilder(ArenaTemplates.Default, definition.Templates, "en", options, true);
        return (new AnswerEvaluator(options, prompts), definition.Create(new GameSetup(1, true, "en")));
    }

    [Fact]
    public void Required_thought_is_enforced_in_every_attempt_including_the_last()
    {
        var (evaluator, game) = Arrange();
        var turn = game.PendingTurns[0] with { Thought = FieldRule.Required };

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"5\"}", null), "x", game, true);

        Assert.Equal("thought", Assert.Single(evaluation.Violations).Key);
    }

    [Fact]
    public void Forbidden_thought_is_rejected_when_given()
    {
        var (evaluator, game) = Arrange();
        var turn = game.PendingTurns[0] with { Thought = FieldRule.Forbidden };

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":\"hm\",\"message\":null,\"move\":\"5\"}", null), "x", game, true);

        Assert.Equal("thought", Assert.Single(evaluation.Violations).Key);
        Assert.Null(evaluation.Thought);
    }

    [Fact]
    public void An_illegal_optional_move_is_not_silently_dropped_in_the_last_attempt()
    {
        var (evaluator, game) = Arrange();
        var turn = game.PendingTurns[0] with { Move = FieldRule.Optional };

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"999\"}", null), "x", game, true);

        Assert.Equal(AnswerClass.Invalid, evaluation.Class);
    }

    [Fact]
    public void Unknown_keys_are_reported_but_do_not_invalidate_the_answer()
    {
        var (evaluator, game) = Arrange();

        var evaluation = evaluator.Evaluate(game.PendingTurns[0], AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"5\",\"admin\":true}", null), "x", game, false);

        Assert.True(evaluation.IsValid);
        Assert.Equal(["admin"], evaluation.ExtraKeys);
    }

    private static TurnRequest Asking(IGame game, FieldRule rule) =>
        game.PendingTurns[0] with { Estimate = rule, EstimateMeaning = TemplateRef.Of("estimate_meaning") };

    private static Evaluation Evaluate(FieldRule rule, string estimateJson, bool last = false)
    {
        var (evaluator, game) = Arrange();
        var answer = AnswerParser.Parse($"{{\"thought\":null,\"message\":null,\"move\":\"5\",\"estimate\":{estimateJson}}}", null);
        return evaluator.Evaluate(Asking(game, rule), answer, "x", game, last);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("37", 37)]
    [InlineData("\"62\"", 62)]
    [InlineData("55.0", 55)]
    public void A_whole_number_from_0_to_100_is_accepted_as_number_or_as_text(string json, int expected)
    {
        var evaluation = Evaluate(FieldRule.Required, json);

        Assert.True(evaluation.IsValid);
        Assert.Equal(expected, evaluation.Estimate);
        Assert.Null(evaluation.DiscardedEstimate);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("42.5")]
    [InlineData("\"high\"")]
    [InlineData("\"50%\"")]
    [InlineData("true")]
    public void An_estimate_outside_the_scale_or_without_a_number_is_rejected_with_a_hint(string json)
    {
        var evaluation = Evaluate(FieldRule.Optional, json);

        var violation = Assert.Single(evaluation.Violations);
        Assert.Equal("estimate", violation.Key);
        Assert.Contains("cannot be used", violation.Violation, StringComparison.Ordinal);
        Assert.Equal("a whole number from 0 to 100", violation.Allowed);
        Assert.Null(evaluation.Estimate);
        Assert.False(evaluation.IsValid);
    }

    [Fact]
    public void A_missing_required_estimate_is_rejected_and_a_missing_optional_one_is_fine()
    {
        var required = Evaluate(FieldRule.Required, "null");
        var optional = Evaluate(FieldRule.Optional, "null");

        Assert.Equal("estimate", Assert.Single(required.Violations).Key);
        Assert.Equal("is missing", required.Violations[0].Violation);
        Assert.True(optional.IsValid);
        Assert.Null(optional.Estimate);
    }

    [Fact]
    public void A_forbidden_estimate_is_rejected_when_given_even_in_the_last_attempt()
    {
        var evaluation = Evaluate(FieldRule.Forbidden, "50", last: true);

        var violation = Assert.Single(evaluation.Violations);
        Assert.Equal(("estimate", "must not be set in this turn, use null"), (violation.Key, violation.Violation));
        Assert.Null(evaluation.Estimate);
    }

    [Theory]
    [InlineData(FieldRule.Required, "null")]
    [InlineData(FieldRule.Required, "\"high\"")]
    [InlineData(FieldRule.Optional, "250")]
    public void In_the_last_attempt_a_missing_or_unusable_estimate_is_discarded_instead_of_rejecting_the_answer(FieldRule rule, string json)
    {
        var evaluation = Evaluate(rule, json, last: true);

        Assert.True(evaluation.IsValid);
        Assert.Empty(evaluation.Violations);
        Assert.Null(evaluation.Estimate);
        Assert.Equal("estimate", evaluation.DiscardedEstimate!.Key);
        Assert.Equal("5", evaluation.Move);
    }

    [Fact]
    public void A_valid_estimate_is_read_even_when_the_move_of_the_same_answer_is_rejected()
    {
        var (evaluator, game) = Arrange();
        var answer = AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"999\",\"estimate\":40}", null);

        var evaluation = evaluator.Evaluate(Asking(game, FieldRule.Required), answer, "x", game, true);

        Assert.Equal("move", Assert.Single(evaluation.Violations).Key);
        Assert.Equal(40, evaluation.Estimate);
    }

    [Fact]
    public void A_turn_without_an_estimate_ignores_the_missing_key()
    {
        var (evaluator, game) = Arrange();

        var evaluation = evaluator.Evaluate(game.PendingTurns[0], AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"5\"}", null), "x", game, false);

        Assert.True(evaluation.IsValid);
        Assert.Null(evaluation.Estimate);
        Assert.Null(evaluation.DiscardedEstimate);
    }
}
