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

public class StrictnessTests
{
    private static readonly LlmShootout.Arena.Debriefing.DebriefingQuestion[] Questions =
    [
        new("scale", LlmShootout.Arena.Debriefing.QuestionKind.Scale, new LlmShootout.Arena.Games.TemplateRef("x"), Min: 1, Max: 5),
        new("flag", LlmShootout.Arena.Debriefing.QuestionKind.Boolean, new LlmShootout.Arena.Games.TemplateRef("x"), WithReason: true),
        new("pick", LlmShootout.Arena.Debriefing.QuestionKind.Choice, new LlmShootout.Arena.Games.TemplateRef("x"), Choices: ["red", "blue"]),
        new("free", LlmShootout.Arena.Debriefing.QuestionKind.Text, new LlmShootout.Arena.Games.TemplateRef("x")),
    ];

    private static (Dictionary<string, string> Answers, List<(string Key, LlmShootout.Arena.Debriefing.QuestionKind Kind, string Problem)> Violations) Check(string json) =>
        LlmShootout.Arena.Debriefing.DebriefAnswers.Validate(Questions, System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());

    [Fact]
    public void A_valid_answer_passes_with_case_insensitive_choices()
    {
        var (answers, violations) = Check("{\"scale\":3,\"flag\":true,\"flag_reason\":\"because\",\"pick\":\"RED\",\"free\":\"text\"}");

        Assert.Empty(violations);
        Assert.Equal("red", answers["pick"]);
        Assert.Equal("true", answers["flag"]);
    }

    [Theory]
    [InlineData("{\"scale\":\"3\",\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":6,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":3.5,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":3,\"flag\":\"ja\",\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "flag")]
    [InlineData("{\"scale\":3,\"flag\":true,\"pick\":\"red\",\"free\":\"t\"}", "flag_reason")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"green\",\"free\":\"t\"}", "pick")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"\"}", "free")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":42}", "free")]
    [InlineData("{\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    public void Wrong_types_values_and_missing_keys_are_violations(string json, string key)
    {
        var (_, violations) = Check(json);

        Assert.Equal(key, Assert.Single(violations).Key);
    }

    [Fact]
    public async Task The_judge_answer_is_validated_before_it_is_used()
    {
        var task = new LlmShootout.Games.SplitOrSteal.PromiseAnnotation(new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition().Templates);
        var request = new LlmShootout.Arena.Annotation.AnnotationRequest("promise-A", LlmShootout.Arena.Games.Seat.A, "p");

        Assert.Null(task.Validate(request, System.Text.Json.Nodes.JsonNode.Parse("{\"announced\":\"split\",\"quote\":null,\"confidence\":0.5}")!.AsObject()));
        Assert.NotNull(task.Validate(request, System.Text.Json.Nodes.JsonNode.Parse("{\"announced\":\"banana\"}")!.AsObject()));
        Assert.NotNull(task.Validate(request, System.Text.Json.Nodes.JsonNode.Parse("{\"announced\":\"split\",\"confidence\":3}")!.AsObject()));
        Assert.NotNull(task.Validate(request, System.Text.Json.Nodes.JsonNode.Parse("{\"announced\":\"split\",\"quote\":5}")!.AsObject()));
        Assert.NotNull(task.Validate(request, System.Text.Json.Nodes.JsonNode.Parse("{\"quote\":\"x\"}")!.AsObject()));
        await Task.CompletedTask;
    }
}

public sealed class FinalReviewTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Facts_of_a_simultaneous_round_are_filed_under_the_turn_of_their_seat()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "9"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "3"));

        var (_, events) = await _harness.RunAsync(new SealedBidDefinition(), a, b);

        var facts = events.OfType<FactEvent>().ToDictionary(f => f.Seat!.Value, f => f.TurnId);
        Assert.Equal("bid-A", facts[Seat.A]);
        Assert.Equal("bid-B", facts[Seat.B]);
    }

    [Fact]
    public async Task The_cost_of_a_failed_call_is_booked_when_the_error_response_reports_it()
    {
        var cost = new CostMeter(10m);
        var player = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y")).Fail("HTTP 500", MatchHarness.Info(0.5m), "raw");

        await Assert.ThrowsAsync<PlayerFailureException>(() => new PlayerGateway(cost).CallAsync(player, new PlayerCall(CallPurpose.Turn, [], null, 1, _ => "{}"), TestContext.Current.CancellationToken));

        Assert.Equal(0.5m, cost.TotalUsd);
        Assert.Equal(0, cost.UnknownCalls);
    }

    [Fact]
    public async Task Judge_entries_keep_prompt_reasoning_call_details_and_raw_exchange()
    {
        var definition = new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition();
        var (outcome, _) = await _harness.RunAsync(definition, new RandomPlayer(1), new RandomPlayer(2), true);
        var judge = new ScriptedPlayer("judge")
            .Reply(_ => new PlayerReply(null, "hmm {\"announced\":\"split\",\"quote\":null,\"confidence\":0.4}", MatchHarness.Info(0.002m), "RAW"))
            .Reply(_ => new PlayerReply("{\"announced\":\"none\",\"quote\":null,\"confidence\":0.1}", null, MatchHarness.Info(0.002m), "RAW2"));

        var path = await new LlmShootout.Arena.Annotation.Annotator(new PlayerGateway(_harness.Cost), _harness.Time).RunAsync(definition.AnnotationTask!, MatchRecord.Load(outcome.Directory), judge, TestContext.Current.CancellationToken);

        var entries = LlmShootout.Arena.Annotation.AnnotationJson.Read(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken))!.Entries;
        Assert.All(entries, e =>
        {
            Assert.NotNull(e.Prompt);
            Assert.Equal(0.002m, e.Call!.CostUsd);
            Assert.NotNull(e.RawExchange);
        });
        Assert.Contains("hmm", entries[0].Reasoning, StringComparison.Ordinal);
        Assert.Equal("split", entries[0].Result!["announced"]!.ToString());
    }

    [Fact]
    public async Task Estimated_cost_appears_in_the_record_the_summary_and_the_report()
    {
        var definition = new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition();
        var estimated = MatchHarness.Info(null) with { EstimatedCostUsd = 0.2m };
        var a = new ScriptedPlayer("a", new LlmShootout.Arena.Identity.ModelIdentity("x/a")).Reply(MatchHarness.Answer(move: "split"), info: estimated);
        var b = new ScriptedPlayer("b", new LlmShootout.Arena.Identity.ModelIdentity("x/b")).Reply(MatchHarness.Answer(move: "split"), info: estimated);

        var (outcome, _) = await _harness.RunAsync(definition, a, b, false);

        var record = MatchRecord.Load(outcome.Directory);
        Assert.Equal(0.4m, record.CostUsd);
        Assert.Equal(2, record.CostEstimatedCalls);
        Assert.Equal(0, record.CostUnknownCalls);
        Assert.Equal(2, LlmShootout.Arena.Reports.MatchReportWriter.Summarize(record).CostEstimatedCalls);
        Assert.Contains("estimated from tokens and model price", LlmShootout.Arena.Reports.MatchReportWriter.BuildReport(definition, record), StringComparison.Ordinal);
    }
}
