using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests;

public sealed class FinalReviewRound2Tests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private sealed class SettingsPlayer : IPlayer
    {
        public string Name => "settings";

        public ModelIdentity? Model => null;

        public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string> { ["temperature"] = "0.3" };

        public Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken) => Task.FromResult(new PlayerReply(request.SampleValidAnswer(new LlmShootout.Arena.Randomness.SeededRng(1))));
    }

    [Fact]
    public async Task An_unexpected_exception_leaves_a_marked_protocol_and_is_rethrown()
    {
        var directory = Path.Combine(_harness.Directory, "boom");
        var plan = new LlmShootout.Arena.Matches.MatchPlan(
            new ThrowingGameDefinition(),
            new GameSetup(1, false, "en"),
            new Dictionary<Seat, IPlayer> { [Seat.A] = new RandomPlayer(1), [Seat.B] = new RandomPlayer(2) },
            new MatchOptions { Debrief = false },
            directory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new LlmShootout.Arena.Matches.MatchRunner(_harness.Cost, _harness.Time).RunAsync(plan, TestContext.Current.CancellationToken));

        var aborted = Assert.IsType<MatchAbortedEvent>(ProtocolReader.ReadAll(directory)[^1]);
        Assert.Equal("internal_error", aborted.Reason);
        Assert.Contains("bug in the game", aborted.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_starting_state_is_recorded_shown_only_to_its_seat_and_kept_for_the_debriefing()
    {
        var a = new ScriptedPlayer("a").Reply(MatchHarness.Answer(move: "4")).Reply(DebriefJsonFor());
        var b = new ScriptedPlayer("b").Reply(MatchHarness.Answer(move: "5")).Reply(DebriefJsonFor());

        var (_, events) = await _harness.RunAsync(new SecretStartDefinition(), a, b, false, new MatchOptions(), debrief: true);

        var cards = events.OfType<FactEvent>().Where(f => f.Kind == "card").ToArray();
        Assert.Equal(2, cards.Length);
        Assert.All(cards, c => Assert.Equal("start", c.TurnId));
        var (cardA, cardB) = (cards.Single(c => c.Seat == Seat.A).Data!["card"], cards.Single(c => c.Seat == Seat.B).Data!["card"]);
        var promptA = string.Join('\n', a.Calls.SelectMany(c => c.Messages).Select(m => m.Text));
        var promptB = string.Join('\n', b.Calls.SelectMany(c => c.Messages).Select(m => m.Text));
        Assert.Contains(cardA, promptA, StringComparison.Ordinal);
        Assert.DoesNotContain(cardB, promptA, StringComparison.Ordinal);
        Assert.Contains(cardB, promptB, StringComparison.Ordinal);
        Assert.DoesNotContain(cardA, promptB, StringComparison.Ordinal);
        Assert.Contains(cardA, a.Calls[^1].Messages[1].Text, StringComparison.Ordinal);
    }

    private static string DebriefJsonFor() => Debriefing.SplitOrStealMatchTests.DebriefJson(communication: false, hidden: true);

    [Fact]
    public async Task The_settings_of_a_player_are_recorded_in_the_match_start()
    {
        var (_, events) = await _harness.RunAsync(new SecretStartDefinition(), new SettingsPlayer(), new RandomPlayer(2));

        var started = Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.Equal("0.3", started.Players[Seat.A].Settings!["temperature"]);
        Assert.Null(started.Players[Seat.B].Settings);
    }

    [Fact]
    public void A_chat_player_describes_its_configured_settings()
    {
        var options = new OpenRouterOptions { ApiKey = "k", Temperature = 0.2f, ReasoningEffort = "low", MaxOutputTokens = 500, CallTimeout = TimeSpan.FromSeconds(90) };
        var http = OpenRouterClientFactory.CreateHttpClient(options, new Tests.OpenRouter.FakeOpenRouterHandler());
        var player = new ChatPlayer(OpenRouterClientFactory.CreateChatClient(options, "x/y", http), "x/y", options, false, TimeProvider.System);

        Assert.Equal(("0.2", "low", "500", "90"), (player.Settings["temperature"], player.Settings["reasoningEffort"], player.Settings["maxOutputTokens"], player.Settings["callTimeoutSeconds"]));
    }

    [Fact]
    public void The_client_level_timeout_spans_every_http_attempt_plus_backoff()
    {
        var options = new OpenRouterOptions { CallTimeout = TimeSpan.FromSeconds(180), MaxHttpRetries = 3, RetryDelay = TimeSpan.FromSeconds(2) };

        var budget = OpenRouterClientFactory.WholeCallBudget(options);

        Assert.True(budget >= TimeSpan.FromSeconds(4 * 180));
        Assert.True(budget > options.CallTimeout + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task With_hidden_identity_no_prompt_of_the_whole_match_names_the_opponent_model_including_hints_and_debriefing()
    {
        var a = new ScriptedPlayer("a", new ModelIdentity("vendor-a/model-a"))
            .Reply("nonsense")
            .Reply(MatchHarness.Answer(move: "split"))
            .Reply(Debriefing.SplitOrStealMatchTests.DebriefJson(communication: false));
        var b = new ScriptedPlayer("b", new ModelIdentity("vendor-b/model-b"))
            .Reply("I cannot do that.")
            .Reply(MatchHarness.Answer(move: "steal"))
            .Reply(Debriefing.SplitOrStealMatchTests.DebriefJson(communication: false));

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
