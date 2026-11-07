using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Prompting;

public sealed class PromptContractTests : IDisposable
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

    [Fact]
    public async Task No_prompt_of_the_whole_match_names_the_opponent_model_including_hints_and_debriefing()
    {
        var a = new ScriptedPlayer("a", new ModelIdentity("vendor-a/model-a"))
            .Reply("nonsense")
            .Reply(MatchHarness.Answer(move: "split"))
            .Reply(Debriefing.SplitOrStealMatchTests.DebriefJson());
        var b = new ScriptedPlayer("b", new ModelIdentity("vendor-b/model-b"))
            .Reply("I cannot do that.")
            .Reply(MatchHarness.Answer(move: "steal"))
            .Reply(Debriefing.SplitOrStealMatchTests.DebriefJson());

        var (outcome, events) = await _harness.RunAsync(new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition(), a, b, false, new MatchOptions(), debrief: true);

        Assert.True(outcome.Completed);
        Assert.Null(outcome.AbortReason);
        Assert.Equal(["invalid", "valid"], events.OfType<ModelCallEvent>().Where(c => c.Seat == Seat.A).Take(2).Select(c => c.Class));
        Assert.Equal("refusal", events.OfType<ModelCallEvent>().First(c => c.Seat == Seat.B).Class);
        Assert.Equal(3, a.Calls.Count);
        Assert.DoesNotContain("vendor-b", string.Join('\n', a.Calls.SelectMany(c => c.Messages).Select(m => m.Text)), StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-a", string.Join('\n', b.Calls.SelectMany(c => c.Messages).Select(m => m.Text)), StringComparison.Ordinal);
    }
}
