using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Players;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Prompting;

public sealed class EstimatePromptTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string Render(IEnumerable<PromptMessage> messages) =>
        string.Join("\n\n", messages.Select(m => $"=== {m.Role} ===\n{m.Text}"));

    private async Task<ScriptedPlayer> PlayFirstTurnAsync(string? estimate, bool withMessages, string language)
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10", estimate: 50));
        var options = estimate is null ? null : new JsonObject { ["estimate"] = estimate };
        await _harness.RunWithTokenAsync(new NumberGuessDefinition(), a, new ScriptedPlayer(), withMessages, new MatchOptions { Debrief = false, MaxAttempts = 1 }, 7, options, LlmShootout.Arena.Games.Seat.A, TestContext.Current.CancellationToken, language);
        return a;
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_prompt_with_a_required_estimate_and_messages_stays_stable(string language)
    {
        var player = await PlayFirstTurnAsync("required", true, language);

        Snapshot.Verify($"prompt-estimate-required.{language}.txt", Render(player.Calls[0].Messages));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_prompt_with_an_optional_estimate_without_messages_stays_stable(string language)
    {
        var player = await PlayFirstTurnAsync("optional", false, language);

        Snapshot.Verify($"prompt-estimate-optional-silent.{language}.txt", Render(player.Calls[0].Messages));
    }

    [Theory]
    [InlineData("required", true, "{\"thought\": ..., \"message\": ..., \"move\": ..., \"estimate\": ...}")]
    [InlineData("optional", false, "{\"thought\": ..., \"move\": ..., \"estimate\": ...}")]
    public async Task The_contract_line_names_the_estimate_as_the_last_key(string rule, bool withMessages, string expectedLine)
    {
        var player = await PlayFirstTurnAsync(rule, withMessages, "en");

        Assert.Contains(expectedLine, player.Calls[0].Messages[1].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "{\"thought\": ..., \"message\": ..., \"move\": ...}")]
    [InlineData(false, "{\"thought\": ..., \"move\": ...}")]
    public async Task Without_an_estimate_the_contract_line_and_the_prompt_do_not_mention_it(bool withMessages, string expectedLine)
    {
        var player = await PlayFirstTurnAsync(null, withMessages, "en");

        var text = player.Calls[0].Messages[1].Text;
        Assert.Contains(expectedLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("estimate", text, StringComparison.OrdinalIgnoreCase);
    }
}
