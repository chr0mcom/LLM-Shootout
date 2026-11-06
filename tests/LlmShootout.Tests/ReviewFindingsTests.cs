using LlmShootout.Arena;
using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Templates;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests;

public sealed class ReviewFindingsTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string PromptText(ScriptedPlayer player) =>
        string.Join("\n", player.Calls.SelectMany(c => c.Messages).Select(m => m.Text));

    [Fact]
    public async Task Without_communication_prompts_and_schema_do_not_mention_messages()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        await _harness.RunAsync(new NumberGuessDefinition(), a, b, communication: false, options: new MatchOptions { MaxAttempts = 1 });

        Assert.DoesNotContain("message", PromptText(a), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", a.Calls[0].Schema!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_communication_prompts_and_schema_contain_the_message_field()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        await _harness.RunAsync(new NumberGuessDefinition(), a, new ScriptedPlayer(), communication: true, options: new MatchOptions { MaxAttempts = 1 });

        Assert.Contains("\"message\"", PromptText(a), StringComparison.Ordinal);
        Assert.Contains("message", a.Calls[0].Schema!.ToJsonString(), StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task A_refusal_that_only_appears_in_the_reasoning_channel_is_labelled_refusal()
    {
        var a = new ScriptedPlayer().Reply(_ => new PlayerReply(null, "I cannot take part in this game, sorry.")).Reply(MatchHarness.Answer(move: "5"));
        var b = new ScriptedPlayer();

        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), a, b);

        Assert.Equal("refusal", events.OfType<ModelCallEvent>().First().Class);
    }

    [Fact]
    public async Task A_technical_failure_keeps_call_details_and_the_raw_exchange_in_the_protocol()
    {
        var info = MatchHarness.Info(null);
        var a = new ScriptedPlayer().Fail("HTTP 500", info, "RAW BODY");

        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), a, new ScriptedPlayer());

        var call = events.OfType<ModelCallEvent>().Single();
        Assert.Equal("test/model", call.Call!.RequestedModel);
        Assert.Equal("RAW BODY", call.RawExchange);
    }

    [Fact]
    public async Task The_gateway_books_unknown_cost_for_failed_model_calls_only()
    {
        var cost = new CostMeter(10m);
        var gateway = new PlayerGateway(cost);
        var request = new PlayerCall(CallPurpose.Turn, [], null, 1, _ => "{}");
        var model = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y")).Timeout();
        var plain = new ScriptedPlayer("p").Timeout();

        await Assert.ThrowsAsync<PlayerFailureException>(() => gateway.CallAsync(plain, request, TestContext.Current.CancellationToken));
        Assert.Equal(0, cost.UnknownCalls);
        await Assert.ThrowsAsync<PlayerFailureException>(() => gateway.CallAsync(model, request, TestContext.Current.CancellationToken));
        Assert.Equal(1, cost.UnknownCalls);
    }

    [Fact]
    public async Task The_match_start_records_version_and_commit()
    {
        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), new RandomPlayer(1), new RandomPlayer(2));

        var started = Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.False(string.IsNullOrWhiteSpace(started.AppVersion));
        Assert.Matches("^([0-9a-f]{12}|unknown)$", started.GitCommit);
    }
}
