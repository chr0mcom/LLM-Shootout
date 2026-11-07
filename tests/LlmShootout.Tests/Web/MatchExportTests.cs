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
}
