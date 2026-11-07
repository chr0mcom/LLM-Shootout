using LlmShootout.Arena;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests.ModelAccess;

/// <summary>
/// Talks to the real OpenRouter. Runs only with OPENROUTER_API_KEY and LLMSHOOTOUT_LIVE_TESTS=1; costs a few cents.
/// The model can be changed with LLMSHOOTOUT_LIVE_MODEL.
/// </summary>
public class LiveOpenRouterTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("LLMSHOOTOUT_LIVE_TESTS") == "1"
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointOptions.OpenRouterKeyVariable));

    [Fact]
    public async Task A_cheap_model_plays_a_short_game_and_the_protocol_has_cost_and_ids()
    {
        Assert.SkipUnless(Enabled, "Set OPENROUTER_API_KEY and LLMSHOOTOUT_LIVE_TESTS=1 to run live tests.");
        var slug = Environment.GetEnvironmentVariable("LLMSHOOTOUT_LIVE_MODEL") ?? "openai/gpt-oss-20b";
        var options = new EndpointOptions { ApiKey = Environment.GetEnvironmentVariable(EndpointOptions.OpenRouterKeyVariable) };
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var catalog = new ModelCatalog(new HttpClient { BaseAddress = options.BaseAddress }, options, TimeProvider.System);
        var factory = new PlayerFactory(options, catalog, TimeProvider.System);
        var player = await factory.CreateAsync(slug, 1, TestContext.Current.CancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), "llm-shootout-live", Guid.NewGuid().ToString("N"));

        try
        {
            var plan = new MatchPlan(
                new SealedBidDefinition(),
                new LlmShootout.Arena.Games.GameSetup(1, false, "en"),
                new Dictionary<LlmShootout.Arena.Games.Seat, IPlayer> { [LlmShootout.Arena.Games.Seat.A] = player, [LlmShootout.Arena.Games.Seat.B] = new RandomPlayer(2) },
                new MatchOptions { MaxCostUsd = 0.10m },
                directory);

            var outcome = await new MatchRunner(new CostMeter(0.10m), time).RunAsync(plan, TestContext.Current.CancellationToken);

            Assert.True(outcome.Completed, outcome.AbortDetail);
            var call = ProtocolReader.ReadAll(directory).OfType<ModelCallEvent>().First();
            Assert.NotNull(call.Call?.GenerationId);
            Assert.NotNull(call.Call?.CostUsd);
            Assert.DoesNotContain(options.ApiKey!, call.RawExchange ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
