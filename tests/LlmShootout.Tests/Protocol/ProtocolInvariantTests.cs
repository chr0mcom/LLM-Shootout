using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests.Protocol;

public sealed class ProtocolInvariantTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Every_event_type_survives_serialization_without_loss()
    {
        var at = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var info = MatchHarness.Info(0.01m);
        var players = new Dictionary<Seat, PlayerDescription> { [Seat.A] = new("random", null), [Seat.B] = new("openrouter:x/y", "x/y") };
        MatchEvent[] events =
        [
            new MatchStartedEvent(at, "g", 1, "de", true, Seat.A, new System.Text.Json.Nodes.JsonObject { ["pot"] = 10 }, players, new Dictionary<string, string> { ["arena"] = "h" }, new MatchOptions(), "1.0.0", "abc"),
            new ModelCallEvent(at, "t1", LlmShootout.Arena.Players.CallPurpose.Turn, Seat.A, 2, [new(LlmShootout.Arena.Players.PromptRole.User, "Grüße ä ö ü ß")], "{\"move\":\"x\"}", "think", new ParsedFields("t", "m", "x", true, ["extra"]), "valid", ["v"], info, false, null, "raw"),
            new MessageEvent(at, "t1", Seat.B, "Hallo", false, true, ["opponent:Provider:x"]),
            new TurnEvent(at, "t1", "k", Seat.A, "move", "played", "thought", true, 2, 1, 0, 1),
            new FactEvent(at, "t1", "kind", FactAudience.Seat, Seat.B, "text", new TemplateRef("key", new Dictionary<string, string> { ["a"] = "b" }), new Dictionary<string, string> { ["d"] = "1" }),
            new MatchFinishedEvent(at, new GameResult("o", new Dictionary<Seat, SeatResult> { [Seat.A] = new(1.5, "win"), [Seat.B] = new(0, "loss") }, new Dictionary<string, string> { ["pot"] = "10" }), 0.0123m, 1),
            new MatchAbortedEvent(at, "cost_limit", "detail"),
            new DebriefEvent(at, Seat.A, true, new Dictionary<string, string> { ["strategy"] = "text" }, [], 1),
        ];

        foreach (var original in events)
        {
            var line = ProtocolJson.ToLine(original);
            var read = ProtocolJson.FromLine(line);

            Assert.Equal(original.GetType(), read.GetType());
            Assert.Equal(line, ProtocolJson.ToLine(read));
            Assert.DoesNotContain('\n', line);
        }

        Assert.Contains("Grüße ä ö ü ß", ProtocolJson.ToLine(events[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Building_the_report_never_changes_the_protocol()
    {
        var (outcome, _) = await _harness.RunAsync(new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition(), new RandomPlayer(1), new RandomPlayer(2), true, new MatchOptions());
        var path = Path.Combine(outcome.Directory, ProtocolFiles.Events);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        await LlmShootout.Arena.Reports.MatchReportWriter.WriteAsync(new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition(), MatchRecord.Load(outcome.Directory), TestContext.Current.CancellationToken);

        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(outcome.Directory, ProtocolFiles.Report)));
    }
}
