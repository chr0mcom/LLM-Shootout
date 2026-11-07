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
}
