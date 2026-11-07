using System.Text.Json.Nodes;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Tests.TestGames;
using LlmShootout.Web.Matches;

namespace LlmShootout.Tests.Web;

public sealed class MatchExportTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<MatchRecord> PlayAsync(params string[] firstMoves)
    {
        var a = new ScriptedPlayer();
        foreach (var move in firstMoves)
        {
            a.Reply(MatchHarness.Answer(move: move));
        }

        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "3"));
        var options = new JsonObject { ["stones"] = 5, ["temptation"] = "pass" };
        var (outcome, _) = await _harness.RunAsync(new TakeAwayDefinition(), a, b, gameOptions: options);
        return MatchRecord.Load(outcome.Directory);
    }

    private static string? Cell(Sheet sheet, string item) =>
        sheet.Rows.Single(row => Equals(row[0], item))[1]?.ToString();

    [Fact]
    public async Task The_overview_counts_the_rule_violations_per_seat_and_marks_the_result()
    {
        var match = await PlayAsync("pass", "2");

        var overview = MatchExport.Overview(match);

        Assert.Equal("yes", Cell(overview, "Result with rule violation"));
        Assert.Equal("1", Cell(overview, "Seat A: rule violations"));
        Assert.Equal("0", Cell(overview, "Seat B: rule violations"));
    }

    [Fact]
    public async Task The_overview_of_a_match_without_a_violation_says_no()
    {
        var match = await PlayAsync("resign");

        var overview = MatchExport.Overview(match);

        Assert.Equal("no", Cell(overview, "Result with rule violation"));
        Assert.Equal("0", Cell(overview, "Seat A: rule violations"));
    }

    [Fact]
    public async Task The_overview_shows_the_figures_for_won_positions_and_the_estimates_per_seat()
    {
        var a = new ScriptedPlayer()
            .Reply(MatchHarness.Answer(move: "1", estimate: 80))
            .Reply(MatchHarness.Answer(move: "2", estimate: 20))
            .Reply(MatchHarness.Answer(move: "3", estimate: 10));
        var b = new ScriptedPlayer()
            .Reply(MatchHarness.Answer(move: "1", estimate: 60))
            .Reply(MatchHarness.Answer(move: "2", estimate: 90))
            .Reply(MatchHarness.Answer(move: "1", estimate: 100));
        var options = new JsonObject { ["stones"] = 10, ["estimate"] = "required" };
        var (outcome, _) = await _harness.RunAsync(new TakeAwayDefinition(), a, b, gameOptions: options);

        var overview = MatchExport.Overview(MatchRecord.Load(outcome.Directory));

        Assert.Equal(["1", "3"], new[] { Cell(overview, "Seat A: moves in a won position"), Cell(overview, "Seat B: moves in a won position") });
        Assert.Equal(["0", "3"], new[] { Cell(overview, "Seat A: optimal moves in a won position"), Cell(overview, "Seat B: optimal moves in a won position") });
        Assert.Equal(["1", "0"], new[] { Cell(overview, "Seat A: won positions given up"), Cell(overview, "Seat B: won positions given up") });
        Assert.Equal(["move-1", "none"], new[] { Cell(overview, "Seat A: turn of the first mistake"), Cell(overview, "Seat B: turn of the first mistake") });
        Assert.Equal(["1", "0"], new[] { Cell(overview, "Seat A: mistake rate"), Cell(overview, "Seat B: mistake rate") });
        Assert.Equal(["80", "83.3333"], new[] { Cell(overview, "Seat A: mean estimate in won positions"), Cell(overview, "Seat B: mean estimate in won positions") });
        Assert.Equal(["15", "n/a"], new[] { Cell(overview, "Seat A: mean estimate in lost positions"), Cell(overview, "Seat B: mean estimate in lost positions") });
        Assert.Equal(["0.03", "0.0567"], new[] { Cell(overview, "Seat A: Brier score"), Cell(overview, "Seat B: Brier score") });
        Assert.Equal(["no", "no"], new[] { Cell(overview, "Seat A: rule violation in a lost position, then won"), Cell(overview, "Seat B: rule violation in a lost position, then won") });
    }

    [Fact]
    public async Task The_overview_of_a_game_without_an_oracle_says_not_applicable_for_every_figure()
    {
        var (outcome, _) = await _harness.RunAsync(new NumberGuessDefinition(), new RandomPlayer(1), new RandomPlayer(2));

        var overview = MatchExport.Overview(MatchRecord.Load(outcome.Directory));

        foreach (var item in new[] { "moves in a won position", "optimal moves in a won position", "won positions given up", "turn of the first mistake", "mistake rate", "mean estimate in won positions", "mean estimate in lost positions", "Brier score" })
        {
            Assert.Equal("n/a", Cell(overview, $"Seat A: {item}"));
        }

        Assert.Equal("no", Cell(overview, "Seat A: rule violation in a lost position, then won"));
    }
}
