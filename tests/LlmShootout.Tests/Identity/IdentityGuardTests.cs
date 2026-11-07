using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Identity;

public sealed class IdentityGuardTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static IdentityGuard Guard(string? a, string? b) =>
        new(
            seat => seat == Seat.A ? (a is null ? null : new ModelIdentity(a)) : (b is null ? null : new ModelIdentity(b)),
            new IdentityScannerSettings(ProviderAliases: IdentityAliases.Providers));

    [Fact]
    public void Marks_a_mention_of_the_opponent_model()
    {
        var hits = Guard("anthropic/claude-sonnet", "openai/gpt-4o").Inspect(Seat.A, "I bet you are ChatGPT.");

        Assert.Equal(["opponent:Alias:ChatGPT"], hits.Select(h => h.Replace("Provider", "Alias", StringComparison.Ordinal)).Where(h => h.StartsWith("opponent", StringComparison.Ordinal)));
    }

    [Fact]
    public void Marks_a_self_mention()
    {
        var hits = Guard("anthropic/claude-sonnet", "openai/gpt-4o").Inspect(Seat.A, "As Claude, I play carefully.");

        Assert.Contains(hits, h => h.StartsWith("self:", StringComparison.Ordinal));
    }

    [Fact]
    public void Marks_hits_ambiguous_in_self_play()
    {
        var hits = Guard("openai/gpt-4o", "openai/gpt-4o").Inspect(Seat.A, "Are you GPT-4o too?");

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.StartsWith("ambiguous:", h, StringComparison.Ordinal));
    }

    [Fact]
    public void Marks_a_shared_provider_ambiguous()
    {
        var hits = Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.B, "We are both from OpenAI.");

        Assert.All(hits, h => Assert.StartsWith("ambiguous:", h, StringComparison.Ordinal));
    }

    [Fact]
    public void Finds_nothing_for_players_without_a_model_or_empty_messages()
    {
        Assert.Empty(Guard(null, null).Inspect(Seat.A, "I am Claude"));
        Assert.Empty(Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.A, string.Empty));
        Assert.Empty(Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.A, null));
    }

    [Fact]
    public async Task The_runner_records_hits_next_to_the_unchanged_message()
    {
        var a = new ScriptedPlayer("a", new ModelIdentity("anthropic/claude-sonnet")).Reply(MatchHarness.Answer(message: "Hello fellow Claude", move: "10"));
        var b = new ScriptedPlayer("b", new ModelIdentity("openai/gpt-4o")).Reply(MatchHarness.Answer(message: "Are you Gemini?", move: "10"));

        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), a, b, communication: true, options: new MatchOptions { MaxAttempts = 1 });

        var messages = events.OfType<MessageEvent>().ToArray();
        Assert.Contains(messages[0].IdentityHits, h => h.StartsWith("self:", StringComparison.Ordinal));
        Assert.Equal("Hello fellow Claude", messages[0].Text);
        Assert.Contains("Hello fellow Claude", string.Join(' ', b.Calls.SelectMany(c => c.Messages).Select(m => m.Text)), StringComparison.Ordinal);
    }
}
