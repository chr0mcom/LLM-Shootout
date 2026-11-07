using System.Text.Json.Nodes;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Matches;

public sealed class EstimateMatchTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly NumberGuessDefinition _guess = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Asking(string rule) => new() { ["estimate"] = rule };

    [Fact]
    public async Task The_estimate_of_a_played_turn_is_written_to_the_turn_event()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10", estimate: 80));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "20", estimate: "35"));

        var (_, events) = await _harness.RunAsync(_guess, a, b, gameOptions: Asking("required"));

        Assert.Equal([80, 35], events.OfType<TurnEvent>().Take(2).Select(t => t.Estimate));
        Assert.Equal(["80", "35"], events.OfType<ModelCallEvent>().Take(2).Select(c => c.Parsed!.Estimate));
    }

    [Fact]
    public async Task A_missing_required_estimate_gets_a_correction_hint_and_a_retry()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "10")).Reply(MatchHarness.Answer(move: "10", estimate: 70));

        var (_, events) = await _harness.RunAsync(_guess, a, new ScriptedPlayer(), gameOptions: Asking("required"));

        Assert.Contains("\"estimate\": is missing. Allowed: a whole number from 0 to 100.", a.Calls[1].Messages[^1].Text, StringComparison.Ordinal);
        var turn = events.OfType<TurnEvent>().First();
        Assert.Equal((70, 2, 1), (turn.Estimate, turn.Attempts, turn.InvalidAnswers));
    }

    [Fact]
    public async Task In_the_last_attempt_an_unusable_estimate_is_discarded_and_the_move_is_played()
    {
        var bad = MatchHarness.Answer(move: "10", estimate: "banana");
        var a = new ScriptedPlayer().Reply(bad).Reply(bad).Reply(bad);

        var (_, events) = await _harness.RunAsync(_guess, a, new ScriptedPlayer(), gameOptions: Asking("required"));

        var turn = events.OfType<TurnEvent>().First();
        Assert.Equal(("played", "10", null, 3, 2), (turn.Status, turn.Move, turn.Estimate, turn.Attempts, turn.InvalidAnswers));
        var calls = events.OfType<ModelCallEvent>().Take(3).ToArray();
        Assert.Equal(["invalid", "invalid", "valid"], calls.Select(c => c.Class));
        var problem = Assert.Single(calls[2].Violations);
        Assert.StartsWith("estimate [discarded]: ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Random_players_give_an_estimate_from_0_to_100_and_the_same_seed_gives_the_same_protocol()
    {
        var first = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2), gameOptions: Asking("optional"));
        var second = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2), gameOptions: Asking("optional"));

        var estimates = first.Events.OfType<TurnEvent>().Select(t => t.Estimate).ToArray();
        Assert.All(estimates, estimate => Assert.InRange(estimate!.Value, 0, 100));
        Assert.Equal(first.Events.Select(ProtocolJson.ToLine), second.Events.Select(ProtocolJson.ToLine));
    }

    [Fact]
    public async Task Without_an_estimate_in_the_turn_the_random_player_adds_nothing_and_the_protocol_has_no_estimate()
    {
        var (_, events) = await _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2));

        Assert.All(events.OfType<TurnEvent>(), t => Assert.Null(t.Estimate));
        Assert.All(events.OfType<ModelCallEvent>(), c => Assert.DoesNotContain("estimate", c.RawContent!, StringComparison.Ordinal));
        Assert.DoesNotContain(events.Select(ProtocolJson.ToLine), line => line.Contains("\"estimate\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_turn_that_asks_for_an_estimate_without_naming_its_meaning_is_refused()
    {
        var options = new JsonObject { ["estimate"] = "required", ["withoutMeaning"] = true };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => _harness.RunAsync(_guess, new RandomPlayer(1), new RandomPlayer(2), gameOptions: options));

        Assert.Contains("names no meaning", failure.Message, StringComparison.Ordinal);
    }
}
