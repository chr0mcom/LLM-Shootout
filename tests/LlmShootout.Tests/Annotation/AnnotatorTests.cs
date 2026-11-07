using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests.Annotation;

public sealed class AnnotatorTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

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
}
