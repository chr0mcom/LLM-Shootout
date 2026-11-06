using LlmShootout.Arena.Players;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests;

public sealed class MinimalGameTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task The_minimal_game_of_the_guide_runs_through_the_unchanged_arena_with_debriefing()
    {
        var (outcome, events) = await _harness.RunAsync(new MatchingPenniesDefinition(), new RandomPlayer(1), new RandomPlayer(2), debrief: true);

        Assert.True(outcome.Completed);
        Assert.All(events.OfType<LlmShootout.Arena.Protocol.DebriefEvent>(), d => Assert.True(d.Valid));
    }

    [Fact]
    public async Task The_minimal_game_scores_a_match_for_seat_a()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "heads"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "heads"));

        var (outcome, _) = await _harness.RunAsync(new MatchingPenniesDefinition(), a, b);

        Assert.Equal("match", outcome.Result!.Outcome);
        Assert.Equal(1, outcome.Result.Seats[LlmShootout.Arena.Games.Seat.A].Score);
    }
}
