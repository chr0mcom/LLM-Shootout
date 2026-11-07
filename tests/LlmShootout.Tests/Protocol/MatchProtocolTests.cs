using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Protocol;

public sealed class MatchProtocolTests : IDisposable
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
    public async Task The_match_start_records_version_and_commit()
    {
        var (_, events) = await _harness.RunAsync(new NumberGuessDefinition(), new RandomPlayer(1), new RandomPlayer(2));

        var started = Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.False(string.IsNullOrWhiteSpace(started.AppVersion));
        Assert.Matches("^([0-9a-f]{12}|unknown)$", started.GitCommit);
    }

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

    private static string DebriefJsonFor() => Debriefing.SplitOrStealMatchTests.DebriefJson();

    [Fact]
    public async Task The_settings_of_a_player_are_recorded_in_the_match_start()
    {
        var (_, events) = await _harness.RunAsync(new SecretStartDefinition(), new SettingsPlayer(), new RandomPlayer(2));

        var started = Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.Equal("0.3", started.Players[Seat.A].Settings!["temperature"]);
        Assert.Null(started.Players[Seat.B].Settings);
    }
}
