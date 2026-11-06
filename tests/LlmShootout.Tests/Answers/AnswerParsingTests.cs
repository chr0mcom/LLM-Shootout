using LlmShootout.Arena;
using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Templates;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Answers;

public class AnswerParsingTests
{
    [Fact]
    public void Reads_plain_json()
    {
        var answer = AnswerParser.Parse("{\"thought\":\"t\",\"message\":\"hi\",\"move\":\"split\"}", null);

        Assert.True(answer.HasJson);
        Assert.Equal(("t", "hi", "split"), (answer.Thought, answer.Message, answer.Move));
        Assert.False(answer.FromReasoning);
    }

    [Fact]
    public void Reads_json_inside_a_code_fence_with_text_around_it()
    {
        var answer = AnswerParser.Parse("Sure!\n```json\n{\"thought\":null,\"message\":null,\"move\":\"x\"}\n```\nGood luck.", null);

        Assert.Equal("x", answer.Move);
        Assert.Null(answer.Thought);
    }

    [Fact]
    public void Turns_non_string_moves_into_text_and_empty_strings_into_null()
    {
        var answer = AnswerParser.Parse("{\"thought\":\"  \",\"message\":\"\",\"move\":42,\"extra\":1}", null);

        Assert.Equal("42", answer.Move);
        Assert.Null(answer.Thought);
        Assert.Null(answer.Message);
        Assert.Equal(["extra"], answer.ExtraKeys);
    }

    [Fact]
    public void Falls_back_to_the_reasoning_channel()
    {
        var answer = AnswerParser.Parse("", "I think... {\"thought\":\"t\",\"message\":null,\"move\":\"b\"}");

        Assert.True(answer.FromReasoning);
        Assert.Equal("b", answer.Move);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("no json", "none here either")]
    public void Reports_no_json(string? content, string? reasoning)
    {
        Assert.False(AnswerParser.Parse(content, reasoning).HasJson);
    }

    [Theory]
    [InlineData("I'm sorry, I can't do that.", true)]
    [InlineData("Es tut mir leid, ich kann nicht mitspielen.", true)]
    [InlineData("That’s something I won’t do... I won't.", true)]
    [InlineData("{\"move\": \"split\"}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Detects_refusals(string? text, bool expected)
    {
        Assert.Equal(expected, RefusalDetector.IsRefusal(text, RefusalDetector.DefaultKeywords));
    }

    [Fact]
    public void Refusal_keywords_are_configurable()
    {
        Assert.True(RefusalDetector.IsRefusal("Nope, not today", ["not today"]));
        Assert.False(RefusalDetector.IsRefusal("I cannot", ["not today"]));
    }

    private static (AnswerEvaluator Evaluator, IGame Game, TurnRequest Turn) Arrange(bool communication, MatchOptions? options = null)
    {
        var definition = new NumberGuessDefinition();
        var setup = new GameSetup(1, communication, "en");
        var game = definition.Create(setup);
        var opts = options ?? new MatchOptions();
        var prompts = new PromptBuilder(ArenaTemplates.Default, definition.Templates, "en", opts, communication);
        return (new AnswerEvaluator(opts, prompts), game, game.PendingTurns[0]);
    }

    [Fact]
    public void Missing_json_is_invalid_with_a_generic_violation_list_of_zero()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, ParsedAnswer.None, "gibberish", game, false);

        Assert.Equal(AnswerClass.Invalid, evaluation.Class);
        Assert.Empty(evaluation.Violations);
    }

    [Fact]
    public void Move_rejected_by_the_game_names_the_reason()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\"500\"}", null), "x", game, false);

        Assert.Equal(AnswerClass.Invalid, evaluation.Class);
        Assert.Equal("move", Assert.Single(evaluation.Violations).Key);
        Assert.Contains("outside 1 to 100", evaluation.Violations[0].Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Forbidden_message_is_a_violation()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":\"hello\",\"move\":\"5\"}", null), "x", game, false);

        Assert.Equal("message", Assert.Single(evaluation.Violations).Key);
    }

    [Fact]
    public void Move_is_normalized_by_the_game()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":\" 07 \"}", null), "x", game, false);

        Assert.True(evaluation.IsValid);
        Assert.Equal("7", evaluation.Move);
    }

    [Fact]
    public void Required_move_missing_is_a_violation_even_in_the_last_attempt()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, AnswerParser.Parse("{\"thought\":null,\"message\":null,\"move\":null}", null), "x", game, true);

        Assert.Equal(AnswerClass.Invalid, evaluation.Class);
        Assert.Equal("move", Assert.Single(evaluation.Violations).Key);
    }

    [Fact]
    public void Refusal_wins_over_invalid_when_the_text_declines()
    {
        var (evaluator, game, turn) = Arrange(false);

        var evaluation = evaluator.Evaluate(turn, ParsedAnswer.None, "I cannot help with that.", game, false);

        Assert.Equal(AnswerClass.Refusal, evaluation.Class);
    }
}
