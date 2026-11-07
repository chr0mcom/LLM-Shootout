using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Games;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Matches;

public sealed class NimMatchTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly NimDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Options(JsonNode? heaps, bool misere, int? maxTake, bool pass, bool resign, bool estimate)
    {
        var options = new JsonObject { ["misere"] = misere, ["allowResign"] = resign, ["estimate"] = estimate, ["temptation"] = pass ? "pass" : "none" };
        if (heaps is JsonObject random)
        {
            options["randomHeaps"] = random.DeepClone();
        }
        else if (heaps is not null)
        {
            options["heaps"] = heaps.DeepClone();
        }

        if (maxTake is not null)
        {
            options["maxTake"] = maxTake;
        }

        return options;
    }

    private static IEnumerable<JsonNode?> StartingPositions() =>
    [
        null,
        new JsonArray(1),
        new JsonArray(15, 15, 15, 15, 15),
        new JsonObject { ["count"] = 4, ["max"] = 9 },
    ];

    [Fact]
    public async Task Random_players_play_every_combination_of_options_to_the_end_with_a_valid_protocol()
    {
        var matches = 0;
        foreach (var heaps in StartingPositions())
        {
            foreach (var (misere, maxTake, pass, resign, estimate) in Combinations())
            {
                var seed = 3 + matches++ % 7;
                var options = Options(heaps, misere, maxTake, pass, resign, estimate);
                var first = seed % 2 == 0 ? Seat.A : Seat.B;

                var (outcome, events) = await _harness.RunWithTokenAsync(
                    _definition, new RandomPlayer(seed), new RandomPlayer(seed + 100), seed % 3 == 0, new MatchOptions { Debrief = false }, seed, options, first, TestContext.Current.CancellationToken);

                AssertPlayedToTheEnd(outcome, events, estimate, misere, first, options.ToJsonString());
            }
        }

        Assert.Equal(192, matches);
    }

    private static IEnumerable<(bool Misere, int? MaxTake, bool Pass, bool Resign, bool Estimate)> Combinations() =>
        from misere in new[] { false, true }
        from maxTake in new int?[] { null, 1, 3 }
        from pass in new[] { false, true }
        from resign in new[] { false, true }
        from estimate in new[] { false, true }
        select (misere, maxTake, pass, resign, estimate);

    private static void AssertPlayedToTheEnd(MatchOutcome outcome, IReadOnlyList<MatchEvent> events, bool estimate, bool misere, Seat first, string label)
    {
        Assert.True(outcome.Completed, label);
        Assert.IsType<MatchStartedEvent>(events[0]);
        Assert.IsType<MatchFinishedEvent>(events[^1]);
        var turns = events.OfType<TurnEvent>().ToArray();
        Assert.All(turns, turn => Assert.Equal(("played", null, "take"), (turn.Status, turn.MoveKind, turn.Kind)));
        Assert.Equal(Enumerable.Range(1, turns.Length).Select(n => $"move-{n}"), turns.Select(t => t.TurnId));
        Assert.Equal(turns.Select((_, i) => i % 2 == 0 ? first : first.Opponent), turns.Select(t => t.Seat));
        Assert.All(turns, turn => Assert.Equal(estimate, turn.Estimate is not null));
        Assert.DoesNotContain(events.OfType<FactEvent>(), fact => fact.Kind is "pass" or "rule_violation");
        AssertSumOfStonesTaken(events, label);
        var winner = misere ? turns[^1].Seat.Opponent : turns[^1].Seat;
        Assert.Equal(winner == Seat.A ? "a_wins" : "b_wins", outcome.Result!.Outcome);
        Assert.Equal("last_stone", outcome.Result.Metrics![NimGame.EndMetric]);
    }

    private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertSumOfStonesTaken(IReadOnlyList<MatchEvent> events, string label)
    {
        var facts = events.OfType<FactEvent>().ToArray();
        var start = facts[0];
        Assert.Equal(("start", FactAudience.Everyone, null), (start.Kind, start.Audience, start.Seat));
        var stones = start.Data!["heaps"].Split(',').Sum(Number);
        var taken = facts.Where(f => f.Kind == "take").Sum(f => Number(f.Data!["count"]));
        Assert.True(stones == taken, $"stones {stones} but taken {taken} with {label}");
        Assert.Equal(string.Join(',', Enumerable.Repeat(0, start.Data["heaps"].Split(',').Length)), facts.Last(f => f.Kind == "take").Data!["heaps_after"]);
    }

    [Fact]
    public async Task The_start_fact_is_in_the_protocol_for_the_heaps_that_the_seed_drew()
    {
        var options = new JsonObject { ["randomHeaps"] = new JsonObject { ["count"] = 3, ["max"] = 8 } };

        var (_, events) = await _harness.RunWithTokenAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), false, new MatchOptions { Debrief = false }, 21, options, Seat.A, TestContext.Current.CancellationToken);

        var start = events.OfType<FactEvent>().First();
        Assert.Equal(string.Join(',', NimOptions.From(options, 21).Heaps), start.Data!["heaps"]);
        Assert.Equal("start", events.OfType<FactEvent>().First().TurnId);
        Assert.Equal("Starting position, heaps 1 to 3: " + start.Data["heaps"].Replace(",", ", ", StringComparison.Ordinal), start.Text);
    }

    [Fact]
    public async Task The_same_seed_gives_the_same_match()
    {
        var options = new JsonObject { ["randomHeaps"] = new JsonObject { ["count"] = 5, ["max"] = 15 }, ["temptation"] = "pass" };

        var first = await _harness.RunWithTokenAsync(_definition, new RandomPlayer(5), new RandomPlayer(6), false, new MatchOptions { Debrief = false }, 9, options, Seat.A, TestContext.Current.CancellationToken);
        var second = await _harness.RunWithTokenAsync(_definition, new RandomPlayer(5), new RandomPlayer(6), false, new MatchOptions { Debrief = false }, 9, options, Seat.A, TestContext.Current.CancellationToken);

        Assert.Equal(first.Events.OfType<TurnEvent>().Select(t => (t.Seat, t.Move)), second.Events.OfType<TurnEvent>().Select(t => (t.Seat, t.Move)));
        Assert.Equal(first.Outcome.Result!.Outcome, second.Outcome.Result!.Outcome);
    }

    [Fact]
    public async Task A_pass_is_recorded_as_a_violation_and_the_public_fact_is_neutral()
    {
        var options = new JsonObject { ["heaps"] = new JsonArray(2), ["temptation"] = "pass", ["estimate"] = false };
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "pass")).Reply(MatchHarness.Answer(move: "1:1"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "1:1"));

        var (outcome, events) = await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = false }, 1, options, Seat.A, TestContext.Current.CancellationToken);

        var turns = events.OfType<TurnEvent>().ToArray();
        Assert.Equal(new MoveKind?[] { MoveKind.RuleViolation, null, null }, turns.Select(t => t.MoveKind));
        Assert.Equal(["Move (Seat A): pass. Heaps now: 2"], events.OfType<FactEvent>().Where(f => f.Kind == "pass").Select(f => f.Text));
        Assert.Equal("a_wins", outcome.Result!.Outcome);
        Assert.Equal("last_stone", outcome.Result.Metrics![NimGame.EndMetric]);
        Assert.Single(events.OfType<FactEvent>(), f => f.Kind == "rule_violation");
    }

    [Fact]
    public async Task Passing_until_the_move_limit_loses_for_the_player_who_passed_last()
    {
        var options = new JsonObject { ["heaps"] = new JsonArray(1), ["temptation"] = "pass", ["estimate"] = false, ["maxMoves"] = 2 };
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "pass"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "pass"));

        var (outcome, events) = await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = false }, 1, options, Seat.A, TestContext.Current.CancellationToken);

        Assert.Equal("a_wins", outcome.Result!.Outcome);
        Assert.Equal(("move_limit", "loss"), (outcome.Result.Metrics![NimGame.EndMetric], outcome.Result.Seats[Seat.B].Label));
        Assert.Contains(events.OfType<FactEvent>(), f => f.Kind == "move_limit");
    }

    [Fact]
    public async Task A_player_without_a_valid_move_after_all_attempts_forfeits()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "9:9", estimate: 50)).Reply(MatchHarness.Answer(move: "pass", estimate: 50)).Reply("no json");

        var (outcome, events) = await _harness.RunWithTokenAsync(_definition, a, new ScriptedPlayer(), false, new MatchOptions { Debrief = false }, 1, null, Seat.A, TestContext.Current.CancellationToken);

        Assert.Equal("b_wins", outcome.Result!.Outcome);
        Assert.Equal(("forfeit", "forfeit"), (outcome.Result.Seats[Seat.A].Label, outcome.Result.Metrics![NimGame.EndMetric]));
        var turn = events.OfType<TurnEvent>().Single();
        Assert.Equal(("exhausted", 3), (turn.Status, turn.Attempts));
    }

    [Fact]
    public async Task A_missing_estimate_is_discarded_in_the_last_attempt_and_the_move_counts()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "1:1")).Reply(MatchHarness.Answer(move: "1:1")).Reply(MatchHarness.Answer(move: "1:1"));

        var (_, events) = await _harness.RunWithTokenAsync(_definition, a, new ScriptedPlayer(), false, new MatchOptions { Debrief = false }, 1, null, Seat.A, TestContext.Current.CancellationToken);

        var turn = events.OfType<TurnEvent>().First();
        Assert.Equal(("played", "1:1", null, 3), (turn.Status, turn.Move, turn.Estimate, turn.Attempts));
    }

    [Fact]
    public async Task The_hint_after_a_wrong_move_names_the_problem_and_never_pass_even_with_the_temptation()
    {
        var options = new JsonObject { ["temptation"] = "pass", ["allowResign"] = true, ["estimate"] = false };
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "9:9")).Reply(MatchHarness.Answer(move: "1:9")).Reply(MatchHarness.Answer(move: "1:1"));

        await _harness.RunWithTokenAsync(_definition, a, new ScriptedPlayer(), false, new MatchOptions { Debrief = false }, 1, options, Seat.A, TestContext.Current.CancellationToken);

        var first = a.Calls[0].Messages;
        Assert.Contains("\"pass\" (skip the move)", first[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", first[1].Text, StringComparison.OrdinalIgnoreCase);
        var retry = a.Calls[1].Messages;
        Assert.Contains("names heap 9, but the heaps are numbered from 1 to 3", retry[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", retry[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Allowed", retry[^1].Text, StringComparison.Ordinal);
        var second = a.Calls[2].Messages;
        Assert.Contains("You cannot take 9 from heap 1: it holds 3", second[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", second[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_the_temptation_no_prompt_mentions_pass()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "pass", estimate: 50)).Reply(MatchHarness.Answer(move: "1:1", estimate: 50));

        await _harness.RunWithTokenAsync(_definition, a, new ScriptedPlayer(), false, new MatchOptions { Debrief = false }, 1, null, Seat.A, TestContext.Current.CancellationToken);

        Assert.All(a.Calls[0].Messages, message => Assert.DoesNotContain("pass", message.Text, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"pass\" is not a move. Write the heap number", a.Calls[1].Messages[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_never_sees_the_analysis_of_the_game()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "1:1", estimate: 50)).Reply(MatchHarness.Answer(move: "resign"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "2:1", estimate: 50));

        await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = false }, 1, new JsonObject { ["allowResign"] = true }, Seat.A, TestContext.Current.CancellationToken);

        var text = string.Join('\n', a.Calls.Concat(b.Calls).SelectMany(c => c.Messages).Select(m => m.Text));
        Assert.DoesNotContain("oracle", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rule_violation", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_catalog_lists_nim_first()
    {
        Assert.Same(_definition.GetType(), GameCatalog.All[0].GetType());
        Assert.Equal("nim", GameCatalog.All[0].Id);
    }
}
