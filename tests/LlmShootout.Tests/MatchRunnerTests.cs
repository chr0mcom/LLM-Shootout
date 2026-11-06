using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests;

public sealed class MatchRunnerTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly NumberGuessDefinition _guess = new();

    public void Dispose() => _harness.Dispose();

    private static string AllPromptText(ScriptedPlayer player) =>
        string.Join("\n", player.Calls.SelectMany(c => c.Messages).Select(m => m.Text));

    [Fact]
    public async Task Random_players_finish_a_game_through_the_unchanged_arena()
    {
        var (outcome, events) = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2));

        Assert.True(outcome.Completed);
        Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.IsType<MatchFinishedEvent>(events[^1]);
    }

    [Fact]
    public async Task Same_seed_gives_identical_protocols()
    {
        var first = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2));
        var second = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2));

        Assert.Equal(first.Events.Select(ProtocolJson.ToLine), second.Events.Select(ProtocolJson.ToLine));
    }

    [Fact]
    public async Task Sequential_turns_alternate_starting_with_the_first_seat()
    {
        var (_, events) = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2), firstSeat: Seat.B);

        var seats = events.OfType<TurnEvent>().Select(t => t.Seat).Take(4).ToArray();
        Assert.Equal([Seat.B, Seat.A, Seat.B, Seat.A], seats);
    }

    [Fact]
    public async Task Simultaneous_moves_stay_hidden_until_the_game_has_applied_the_round()
    {
        var definition = new SealedBidDefinition();
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "9"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "3"));

        var (outcome, events) = await _harness.RunAsync(definition, a, b);

        Assert.Equal("a_wins", outcome.Result!.Outcome);
        Assert.DoesNotContain("bid 9", AllPromptText(b), StringComparison.Ordinal);
        Assert.DoesNotContain("bid 3", AllPromptText(a), StringComparison.Ordinal);
        var round = Assert.Single(definition.Created!.AppliedRounds);
        Assert.Equal(2, round.Count);
        Assert.Equal(2, events.OfType<FactEvent>().Count());
    }

    [Fact]
    public async Task Invalid_answer_is_retried_with_previous_answer_and_correction_hint()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "banana")).Reply(MatchHarness.Answer(move: "42"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "100"));

        var (_, events) = await _harness.RunAsync(_guess, a, b, seed: 7);

        var retry = a.Calls[1].Messages;
        Assert.Equal(PromptRole.Assistant, retry[2].Role);
        Assert.Contains("banana", retry[2].Text, StringComparison.Ordinal);
        Assert.Contains("\"move\": \"banana\" is not an integer", retry[3].Text, StringComparison.Ordinal);
        Assert.Equal(["invalid", "valid"], events.OfType<ModelCallEvent>().Where(e => e.Seat == Seat.A).Take(2).Select(e => e.Class));
    }

    [Fact]
    public async Task Refusal_is_labelled_and_gets_a_neutral_hint()
    {
        var a = new ScriptedPlayer().Reply("I'm sorry, but I can't take part in guessing games.").Reply(MatchHarness.Answer(move: "50"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "50"));

        var (_, events) = await _harness.RunAsync(_guess, a, b);

        Assert.Equal("refusal", events.OfType<ModelCallEvent>().First().Class);
        Assert.Contains("no consequences outside the game", a.Calls[1].Messages[^1].Text, StringComparison.Ordinal);
        Assert.Equal(1, events.OfType<TurnEvent>().First().Refusals);
    }

    [Fact]
    public async Task Exhausted_attempts_report_the_status_to_the_game_and_the_match_continues()
    {
        var a = new ScriptedPlayer().Reply("nonsense").Reply("nonsense").Reply("nonsense").Reply(MatchHarness.Answer(move: "50"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "50")).Reply(MatchHarness.Answer(move: "50"));

        var (outcome, events) = await _harness.RunAsync(_guess, a, b, options: new MatchOptions { MaxAttempts = 3 });

        var first = events.OfType<TurnEvent>().First();
        Assert.Equal("exhausted", first.Status);
        Assert.Equal(3, first.Attempts);
        Assert.Equal(3, first.InvalidAnswers);
        Assert.Equal(Seat.B, events.OfType<TurnEvent>().ElementAt(1).Seat);
        Assert.Equal("player_failure", outcome.AbortReason);
    }

    [Fact]
    public async Task Long_message_is_invalid_until_the_last_attempt_where_it_is_shortened()
    {
        var longMessage = string.Join(' ', Enumerable.Repeat("word", 25));
        var a = new ScriptedPlayer()
            .Reply(MatchHarness.Answer(message: longMessage, move: "10"))
            .Reply(MatchHarness.Answer(message: longMessage, move: "10"))
            .Reply(MatchHarness.Answer(message: longMessage, move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        var (_, events) = await _harness.RunAsync(_guess, a, b, communication: true);

        var calls = events.OfType<ModelCallEvent>().Where(e => e.Seat == Seat.A).Take(3).ToArray();
        Assert.Equal(["invalid", "invalid", "valid"], calls.Select(c => c.Class));
        var message = events.OfType<MessageEvent>().First();
        Assert.True(message.Truncated);
        Assert.Equal(20, message.Text!.Split(' ').Length);
    }

    [Fact]
    public async Task Missing_required_message_after_the_last_attempt_becomes_silence()
    {
        var options = new JsonObject { ["messageRequired"] = true };
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10")).Reply(MatchHarness.Answer(move: "10")).Reply(MatchHarness.Answer(move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(message: "hello", move: "10"));

        var (_, events) = await _harness.RunAsync(_guess, a, b, communication: true, gameOptions: options);

        var silence = events.OfType<MessageEvent>().First();
        Assert.True(silence.Silent);
        Assert.Null(silence.Text);
        Assert.Contains("(no message)", AllPromptText(b), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cost_limit_aborts_cleanly_and_leaves_a_valid_protocol()
    {
        using var harness = new MatchHarness(maxCostUsd: 0.01m);
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"), info: MatchHarness.Info(0.02m));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"), info: MatchHarness.Info(0.02m));

        var (outcome, events) = await harness.RunAsync(_guess, a, b);

        Assert.False(outcome.Completed);
        Assert.Equal("cost_limit", outcome.AbortReason);
        Assert.IsType<MatchAbortedEvent>(events[^1]);
        Assert.Single(events.OfType<TurnEvent>());
    }

    [Fact]
    public async Task A_timeout_uses_up_one_attempt()
    {
        var a = new ScriptedPlayer().Timeout().Reply(MatchHarness.Answer(move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        var (_, events) = await _harness.RunAsync(_guess, a, b);

        Assert.Equal("technical_error", events.OfType<ModelCallEvent>().First().Class);
        Assert.Equal(2, events.OfType<TurnEvent>().First().Attempts);
    }

    [Fact]
    public async Task All_attempts_timing_out_aborts_the_match()
    {
        var a = new ScriptedPlayer().Timeout().Timeout().Timeout();

        var (outcome, events) = await _harness.RunAsync(_guess, a, new ScriptedPlayer());

        Assert.Equal("timeout", outcome.AbortReason);
        Assert.IsType<MatchAbortedEvent>(events[^1]);
    }

    [Fact]
    public async Task A_technical_failure_aborts_the_match_with_a_reason()
    {
        var a = new ScriptedPlayer().Fail("HTTP 500");

        var (outcome, events) = await _harness.RunAsync(_guess, a, new ScriptedPlayer());

        Assert.Equal("player_failure", outcome.AbortReason);
        Assert.Contains("HTTP 500", outcome.AbortDetail, StringComparison.Ordinal);
        Assert.Equal("technical_error", events.OfType<ModelCallEvent>().Single().Class);
    }

    [Fact]
    public async Task Cancellation_writes_an_aborted_event_and_rethrows()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var a = new ScriptedPlayer().Reply(_ =>
        {
            cts.Cancel();
            return new PlayerReply(MatchHarness.Answer(move: "10"));
        });
        var directory = Path.Combine(_harness.Directory, "cancelled");
        var plan = new MatchPlan(
            _guess,
            new GameSetup(7, false, "en"),
            new Dictionary<Seat, IPlayer> { [Seat.A] = a, [Seat.B] = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10")) },
            new MatchOptions { Debrief = false },
            directory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MatchRunner(_harness.Cost, _harness.Time).RunAsync(plan, cts.Token));

        var events = ProtocolReader.ReadAll(directory);
        var aborted = Assert.IsType<MatchAbortedEvent>(events[^1]);
        Assert.Equal("cancelled", aborted.Reason);
    }

    [Fact]
    public async Task Answer_hidden_in_the_reasoning_channel_is_recovered()
    {
        var a = new ScriptedPlayer().Reply(reply => new PlayerReply(null, $"thinking... {MatchHarness.Answer(move: "10")}"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        var (_, events) = await _harness.RunAsync(_guess, a, b);

        var call = events.OfType<ModelCallEvent>().First();
        Assert.Equal("valid", call.Class);
        Assert.True(call.Parsed!.FromReasoning);
    }

    [Fact]
    public async Task Long_thought_is_shortened_and_marked_never_rejected()
    {
        var thought = string.Join(' ', Enumerable.Repeat("idea", 60));
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(thought: thought, move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10"));

        var (_, events) = await _harness.RunAsync(_guess, a, b);

        var turn = events.OfType<TurnEvent>().First();
        Assert.True(turn.ThoughtTruncated);
        Assert.Equal(40, turn.Thought!.Split(' ').Length);
        Assert.Equal(1, turn.Attempts);
    }

    [Fact]
    public async Task Thoughts_of_the_opponent_never_reach_a_prompt_but_own_thoughts_come_back()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(thought: "SECRET-A", move: "10")).Reply(MatchHarness.Answer(thought: "SECRET-A2", move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(thought: "SECRET-B", move: "10")).Reply(MatchHarness.Answer(thought: "SECRET-B2", move: "10"));

        await _harness.RunAsync(_guess, a, b, options: new MatchOptions { MaxAttempts = 1 });

        Assert.DoesNotContain("SECRET-A", AllPromptText(b), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-B", AllPromptText(a), StringComparison.Ordinal);
        Assert.Contains("SECRET-A", a.Calls[1].Messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thought_memory_can_be_switched_off()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(thought: "SECRET-A", move: "10")).Reply(MatchHarness.Answer(move: "10"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10")).Reply(MatchHarness.Answer(move: "10"));

        await _harness.RunAsync(_guess, a, b, options: new MatchOptions { ThoughtMemory = 0, MaxAttempts = 1 });

        Assert.DoesNotContain("SECRET-A", a.Calls[1].Messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Private_facts_reach_only_their_seat_and_analysis_facts_no_prompt()
    {
        var definition = new RoleSwapDefinition();
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "6")).Reply(MatchHarness.Answer(move: "accept")).Reply(MatchHarness.Answer(move: "5")).Reply(MatchHarness.Answer(move: "accept"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "accept")).Reply(MatchHarness.Answer(move: "5")).Reply(MatchHarness.Answer(move: "accept"));

        var (outcome, events) = await _harness.RunAsync(definition, a, b, options: new MatchOptions { MaxAttempts = 1 });

        Assert.True(outcome.Completed);
        Assert.Contains("Your private minimum", AllPromptText(b), StringComparison.Ordinal);
        Assert.DoesNotContain("Your private minimum", a.Calls[0].Messages[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Offer fair", AllPromptText(a) + AllPromptText(b), StringComparison.Ordinal);
        Assert.Contains(events.OfType<FactEvent>(), f => f.Audience == FactAudience.Analysis && f.Text.StartsWith("Offer fair", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hidden_identity_keeps_the_opponent_slug_out_of_every_prompt_and_reveal_puts_it_in()
    {
        var a = new ScriptedPlayer("a", new ModelIdentity("vendor-a/model-a")).Reply(MatchHarness.Answer(move: "10"));
        var b = new ScriptedPlayer("b", new ModelIdentity("vendor-b/model-b")).Reply(MatchHarness.Answer(move: "10"));
        await _harness.RunAsync(_guess, a, b, options: new MatchOptions { MaxAttempts = 1 });

        Assert.DoesNotContain("vendor-b", AllPromptText(a), StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-a", AllPromptText(b), StringComparison.Ordinal);

        var a2 = new ScriptedPlayer("a", new ModelIdentity("vendor-a/model-a")).Reply(MatchHarness.Answer(move: "10"));
        var b2 = new ScriptedPlayer("b", new ModelIdentity("vendor-b/model-b")).Reply(MatchHarness.Answer(move: "10"));
        await _harness.RunAsync(_guess, a2, b2, options: new MatchOptions { MaxAttempts = 1, RevealIdentity = true });

        Assert.Contains("vendor-b/model-b", AllPromptText(a2), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protocol_lines_roundtrip_without_loss()
    {
        var (outcome, events) = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2), communication: true);

        var lines = File.ReadAllLines(Path.Combine(outcome.Directory, ProtocolFiles.Events));
        Assert.Equal(lines, events.Select(ProtocolJson.ToLine));
        Assert.All(events, e => Assert.Equal(ProtocolVersion.Current, e.SchemaVersion));
    }

    [Fact]
    public async Task Role_swap_game_runs_through_the_unchanged_arena()
    {
        var (outcome, _) = await _harness.RunAsync(new RoleSwapDefinition(), new RandomPlayer(3), new RandomPlayer(4), communication: true);

        Assert.True(outcome.Completed);
        Assert.Equal("finished", outcome.Result!.Outcome);
    }
}
