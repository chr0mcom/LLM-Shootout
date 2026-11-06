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

    private static IdentityGuard Guard(string? a, string? b, bool redact = false) =>
        new(
            new Dictionary<Seat, ModelIdentity?>
            {
                [Seat.A] = a is null ? null : new ModelIdentity(a),
                [Seat.B] = b is null ? null : new ModelIdentity(b),
            },
            new IdentityScannerSettings(ProviderAliases: IdentityAliases.Providers),
            redact);

    [Fact]
    public void Marks_a_mention_of_the_opponent_model()
    {
        var result = Guard("anthropic/claude-sonnet", "openai/gpt-4o").Inspect(Seat.A, "I bet you are ChatGPT.");

        Assert.Equal(["opponent:Alias:ChatGPT"], result.Hits.Select(h => h.Replace("Provider", "Alias", StringComparison.Ordinal)).Where(h => h.StartsWith("opponent", StringComparison.Ordinal)));
        Assert.Equal("I bet you are ChatGPT.", result.Text);
    }

    [Fact]
    public void Marks_a_self_mention()
    {
        var result = Guard("anthropic/claude-sonnet", "openai/gpt-4o").Inspect(Seat.A, "As Claude, I keep promises.");

        Assert.Contains(result.Hits, h => h.StartsWith("self:", StringComparison.Ordinal));
    }

    [Fact]
    public void Marks_hits_ambiguous_in_self_play()
    {
        var result = Guard("openai/gpt-4o", "openai/gpt-4o").Inspect(Seat.A, "Are you GPT-4o too?");

        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, h => Assert.StartsWith("ambiguous:", h, StringComparison.Ordinal));
    }

    [Fact]
    public void Marks_a_shared_provider_ambiguous()
    {
        var result = Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.B, "We are both from OpenAI.");

        Assert.All(result.Hits, h => Assert.StartsWith("ambiguous:", h, StringComparison.Ordinal));
    }

    [Fact]
    public void Redacts_only_when_asked()
    {
        var plain = Guard("anthropic/claude-sonnet", "openai/gpt-4o").Inspect(Seat.A, "You are GPT-4o");
        var redacted = Guard("anthropic/claude-sonnet", "openai/gpt-4o", redact: true).Inspect(Seat.A, "You are GPT-4o");

        Assert.Equal("You are GPT-4o", plain.Text);
        Assert.Equal("You are [IDENTITY REDACTED]", redacted.Text);
    }

    [Fact]
    public void Finds_nothing_for_players_without_a_model_or_empty_messages()
    {
        Assert.Empty(Guard(null, null).Inspect(Seat.A, "I am Claude").Hits);
        Assert.Empty(Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.A, string.Empty).Hits);
        Assert.Null(Guard("openai/gpt-4o", "openai/o3").Inspect(Seat.A, null).Text);
    }

    [Fact]
    public async Task The_runner_records_hits_next_to_the_message_and_redacts_on_request()
    {
        var a = new ScriptedPlayer("a", new ModelIdentity("anthropic/claude-sonnet")).Reply(MatchHarness.Answer(message: "Hello fellow Claude", move: "10"));
        var b = new ScriptedPlayer("b", new ModelIdentity("openai/gpt-4o")).Reply(MatchHarness.Answer(message: "Are you Gemini?", move: "10"));

        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), a, b, communication: true, options: new MatchOptions { RedactIdentity = true, MaxAttempts = 1 });

        var messages = events.OfType<MessageEvent>().ToArray();
        Assert.Contains(messages[0].IdentityHits, h => h.StartsWith("self:", StringComparison.Ordinal));
        Assert.Equal("Hello fellow [IDENTITY REDACTED]", messages[0].Text);
        Assert.Contains("Hello fellow [IDENTITY REDACTED]", b.Calls[0].Messages[1].Text + string.Join(' ', b.Calls.SelectMany(c => c.Messages).Select(m => m.Text)), StringComparison.Ordinal);
    }
}
