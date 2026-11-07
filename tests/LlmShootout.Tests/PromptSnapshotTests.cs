using LlmShootout.Arena;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Games.SplitOrSteal;
using LlmShootout.Tests.Debriefing;

namespace LlmShootout.Tests;

public sealed class PromptSnapshotTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly SplitOrStealDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static string Render(IEnumerable<PromptMessage> messages) =>
        string.Join("\n\n", messages.Select(m => $"=== {m.Role} ===\n{m.Text}"));

    private static ScriptedPlayer Player(params string[] replies)
    {
        var player = new ScriptedPlayer("p", new ModelIdentity("vendor/model"));
        foreach (var reply in replies)
        {
            player.Reply(reply);
        }

        return player;
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_prompts_of_a_match_with_messages_stay_stable(string language)
    {
        var a = Player(
            MatchHarness.Answer("Ich beobachte.", "Du bist dran.", null),
            MatchHarness.Answer("Wieder.", "Interessante Stellung.", null),
            MatchHarness.Answer("Noch einmal.", "Machen wir weiter.", null),
            "no json at all",
            MatchHarness.Answer("Ich wähle.", null, "split"),
            SplitOrStealMatchTests.DebriefJson());
        var b = Player(
            MatchHarness.Answer("Mal sehen.", "Verstanden.", null),
            MatchHarness.Answer("Hm.", "Schönes Spiel.", null),
            MatchHarness.Answer("Gut.", "Es geht weiter.", null),
            MatchHarness.Answer("Ich wähle.", null, "steal"),
            SplitOrStealMatchTests.DebriefJson());

        await _harness.RunWithTokenAsync(_definition, a, b, true, new MatchOptions(), 3, null, LlmShootout.Arena.Games.Seat.A, TestContext.Current.CancellationToken, language);

        Snapshot.Verify($"prompt-first-talk.{language}.txt", Render(a.Calls[0].Messages));
        Snapshot.Verify($"prompt-second-talk.{language}.txt", Render(b.Calls[0].Messages));
        Snapshot.Verify($"prompt-decide-with-retry.{language}.txt", Render(a.Calls[4].Messages));
        Snapshot.Verify($"prompt-debrief.{language}.txt", Render(a.Calls[5].Messages));
    }

    [Fact]
    public async Task The_prompt_without_messages_has_no_trace_of_messages()
    {
        var a = Player(MatchHarness.Answer("t", null, "split"));
        var b = Player(MatchHarness.Answer("t", null, "split"));

        await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = false }, 3, null, LlmShootout.Arena.Games.Seat.A, TestContext.Current.CancellationToken, "en");

        Snapshot.Verify("prompt-decide-silent.en.txt", Render(a.Calls[0].Messages));
    }
}
