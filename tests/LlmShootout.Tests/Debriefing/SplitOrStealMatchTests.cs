using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Games.SplitOrSteal;

namespace LlmShootout.Tests.Debriefing;

public sealed class SplitOrStealMatchTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly SplitOrStealDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    public static string DebriefJson(bool communication = true, string truthful = "true", int trust = 4)
    {
        var json = new JsonObject
        {
            ["strategy"] = "I wanted fair play.",
            ["opponent_strategy"] = "Seemed cooperative.",
            ["trust_in_opponent"] = trust,
            ["tried_to_deceive"] = false,
            ["tried_to_deceive_reason"] = "No need.",
            ["next_time"] = "Same.",
        };
        if (communication)
        {
            json["announced_truthfully"] = truthful == "true";
            json["announced_truthfully_reason"] = "I said what I did.";
        }

        return json.ToJsonString();
    }

    private static ScriptedPlayer Player(string name, string slug, string decision, string thought, string[] messages, string? debrief)
    {
        var player = new ScriptedPlayer(name, new ModelIdentity(slug));
        foreach (var message in messages)
        {
            player.Reply(MatchHarness.Answer(thought, message));
        }

        player.Reply(MatchHarness.Answer(thought, move: decision));
        return debrief is null ? player : player.Reply(debrief);
    }

    private static string[] Said(string who) => [$"{who} says hello", $"{who} promises to split", $"{who} again: split"];

    private static string Prompts(ScriptedPlayer player, int from) =>
        string.Join("\n", player.Calls.Skip(from).SelectMany(c => c.Messages).Select(m => m.Text));

    private async Task<(MatchOutcome Outcome, IReadOnlyList<MatchEvent> Events, ScriptedPlayer A, ScriptedPlayer B)> PlayAsync(
        bool communication = true,
        string? debriefA = "",
        string? debriefB = "")
    {
        var a = Player("a", "vendor-a/model-a", "split", "SECRET-A", communication ? Said("A") : [], debriefA == "" ? DebriefJson(communication) : debriefA);
        var b = Player("b", "vendor-b/model-b", "steal", "SECRET-B", communication ? Said("B") : [], debriefB == "" ? DebriefJson(communication) : debriefB);
        var (outcome, events) = await _harness.RunAsync(_definition, a, b, communication, new MatchOptions(), debrief: true);
        return (outcome, events, a, b);
    }

    [Fact]
    public async Task A_full_match_with_communication_asks_all_questions()
    {
        var (outcome, events, _, _) = await PlayAsync();

        Assert.Equal("b_stole", outcome.Result!.Outcome);
        var debriefs = events.OfType<DebriefEvent>().ToArray();
        Assert.Equal([Seat.A, Seat.B], debriefs.Select(d => d.Seat));
        Assert.All(debriefs, d => Assert.True(d.Valid));
        Assert.Equal(
            ["announced_truthfully", "announced_truthfully_reason", "next_time", "opponent_strategy", "strategy", "tried_to_deceive", "tried_to_deceive_reason", "trust_in_opponent"],
            debriefs[0].Answers.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Without_communication_the_game_asks_no_extra_question()
    {
        var (_, events, _, _) = await PlayAsync(communication: false);

        var keys = events.OfType<DebriefEvent>().First().Answers.Keys;
        Assert.DoesNotContain("announced_truthfully", keys);
        Assert.Contains("tried_to_deceive", keys);
    }

    [Fact]
    public async Task The_debriefing_shows_a_player_only_what_it_saw_plus_its_own_thoughts()
    {
        var (_, _, a, b) = await PlayAsync();

        var debriefA = Prompts(a, 4);
        Assert.Contains("SECRET-A", debriefA, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-B", debriefA, StringComparison.Ordinal);
        Assert.Contains("B says hello", debriefA, StringComparison.Ordinal);
        Assert.Contains("Payout (Your opponent): 100 points", debriefA, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-b", debriefA, StringComparison.Ordinal);
        var debriefB = Prompts(b, 4);
        Assert.DoesNotContain("SECRET-A", debriefB, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-a", debriefB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_correction_hint_names_the_key_and_the_range_and_a_second_try_can_succeed()
    {
        var a = Player("a", "vendor-a/model-a", "split", "t", Said("A"), DebriefJson(trust: 9)).Reply(DebriefJson());
        var b = Player("b", "vendor-b/model-b", "steal", "t", Said("B"), DebriefJson());

        var (_, events) = await _harness.RunAsync(_definition, a, b, true, new MatchOptions(), debrief: true);

        var retry = a.Calls[^1].Messages;
        Assert.Contains("trust_in_opponent", retry[^1].Text, StringComparison.Ordinal);
        Assert.Contains("from 1 to 5", retry[^1].Text, StringComparison.Ordinal);
        var debrief = events.OfType<DebriefEvent>().First(d => d.Seat == Seat.A);
        Assert.True(debrief.Valid);
        Assert.Equal(2, debrief.Attempts);
    }

    [Fact]
    public async Task After_the_retries_a_failed_debriefing_is_marked_failed_and_the_result_stays()
    {
        var a = Player("a", "vendor-a/model-a", "split", "t", Said("A"), "not json").Reply("still not json").Reply("{}");
        var b = Player("b", "vendor-b/model-b", "steal", "t", Said("B"), DebriefJson());

        var (outcome, events) = await _harness.RunAsync(_definition, a, b, true, new MatchOptions(), debrief: true);

        Assert.True(outcome.Completed);
        Assert.Null(outcome.AbortReason);
        var debrief = events.OfType<DebriefEvent>().First(d => d.Seat == Seat.A);
        Assert.False(debrief.Valid);
        Assert.Equal(3, debrief.Attempts);
        Assert.NotEmpty(debrief.Problems);
    }

    [Fact]
    public async Task A_technical_failure_during_the_debriefing_does_not_discard_the_result()
    {
        var (outcome, events, _, _) = await PlayAsync(debriefA: null);

        Assert.True(outcome.Completed);
        var failed = events.OfType<DebriefEvent>().First(d => d.Seat == Seat.A);
        Assert.False(failed.Valid);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains(events.OfType<ModelCallEvent>(), c => c.Purpose == CallPurpose.Debrief && c.Class == "technical_error");
    }

    [Fact]
    public async Task Random_players_answer_the_whole_debriefing_validly()
    {
        var (outcome, events) = await _harness.RunAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), true, new MatchOptions(), debrief: true);

        Assert.True(outcome.Completed);
        Assert.All(events.OfType<DebriefEvent>(), d => Assert.True(d.Valid, string.Join(';', d.Problems)));
    }

    [Fact]
    public async Task Repeating_the_debriefing_writes_a_separate_file_and_leaves_the_protocol_untouched()
    {
        var (outcome, _) = await _harness.RunAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), true, new MatchOptions());
        var path = Path.Combine(outcome.Directory, ProtocolFiles.Events);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        var record = MatchRecord.Load(outcome.Directory);
        await using (var writer = new ProtocolWriter(outcome.Directory, "debrief.rerun.jsonl"))
        {
            var players = new Dictionary<Seat, IPlayer> { [Seat.A] = new RandomPlayer(5), [Seat.B] = new RandomPlayer(6) };
            await new Debriefer(new PlayerGateway(_harness.Cost), _harness.Time).RunAsync(_definition, record, players, new MatchOptions(), writer, TestContext.Current.CancellationToken);
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, ProtocolReader.ReadAll(outcome.Directory, "debrief.rerun.jsonl").OfType<DebriefEvent>().Count());
    }

    [Fact]
    public async Task Budget_exhaustion_during_the_debriefing_aborts_with_cost_limit_but_keeps_the_result()
    {
        using var harness = new MatchHarness(maxCostUsd: 0.05m);
        var a = new ScriptedPlayer("a", new ModelIdentity("vendor-a/model-a")).Reply(MatchHarness.Answer(move: "split"), info: MatchHarness.Info(0.03m)).Reply(DebriefJson(false));
        var b = new ScriptedPlayer("b", new ModelIdentity("vendor-b/model-b")).Reply(MatchHarness.Answer(move: "steal"), info: MatchHarness.Info(0.03m)).Reply(DebriefJson(false));

        var (outcome, events) = await harness.RunAsync(_definition, a, b, false, new MatchOptions(), debrief: true);

        Assert.NotNull(outcome.Result);
        Assert.Equal("cost_limit", outcome.AbortReason);
        Assert.IsType<MatchFinishedEvent>(events.OfType<MatchFinishedEvent>().Single());
        Assert.IsType<MatchAbortedEvent>(events[^1]);
        var debrief = Assert.Single(events.OfType<DebriefEvent>());
        Assert.False(debrief.Valid);
        Assert.Contains("Cost limit", debrief.Problems[0], StringComparison.Ordinal);
        Assert.Single(a.Calls);
        Assert.Equal(MatchRecord.Load(outcome.Directory).Result!.Outcome, outcome.Result!.Outcome);
    }
}
