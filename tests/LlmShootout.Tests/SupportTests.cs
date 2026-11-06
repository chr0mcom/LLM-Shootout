using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests;

public class SupportTests
{
    [Fact]
    public void Seat_extension_members_work()
    {
        Assert.Equal(Seat.B, Seat.A.Opponent);
        Assert.Equal(Seat.A, Seat.B.Opponent);
        Assert.Equal([Seat.A, Seat.B], Seat.All);
    }

    [Fact]
    public void Cost_meter_sums_known_costs_counts_unknown_and_enforces_the_limit()
    {
        var meter = new CostMeter(0.05m);
        meter.Record(0.02m);
        meter.Record(null);
        meter.EnsureWithinLimit();
        meter.Record(0.03m);

        Assert.Equal(0.05m, meter.TotalUsd);
        Assert.Equal(1, meter.UnknownCalls);
        var ex = Assert.Throws<BudgetExceededException>(meter.EnsureWithinLimit);
        Assert.Contains("limit 0.05", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_shows_each_seat_only_what_it_may_see()
    {
        var timeline = new Timeline();
        timeline.AddMessage("t1", Seat.A, "hi");
        timeline.AddFact("t1", new GameFact("pub", FactAudience.Everyone, Seat.A, new TemplateRef("x")));
        timeline.AddFact("t1", new GameFact("priv", FactAudience.Seat, Seat.B, new TemplateRef("y")));
        timeline.AddFact("t1", new GameFact("ana", FactAudience.Analysis, Seat.A, new TemplateRef("z")));

        Assert.Equal(["pub", "priv"], timeline.VisibleTo(Seat.B).OfType<FactEntry>().Select(f => f.Fact.Kind));
        Assert.Equal(["pub"], timeline.VisibleTo(Seat.A).OfType<FactEntry>().Select(f => f.Fact.Kind));
        Assert.Equal(4, timeline.Entries.Count);
    }

    [Fact]
    public void Thought_log_returns_only_own_recent_thoughts()
    {
        var log = new ThoughtLog();
        log.Add(Seat.A, "a1");
        log.Add(Seat.B, "b1");
        log.Add(Seat.A, "a2");
        log.Add(Seat.A, "a3");

        Assert.Equal(["a2", "a3"], log.Recent(Seat.A, 2));
        Assert.Empty(log.Recent(Seat.A, 0));
        Assert.Equal(["b1"], log.Recent(Seat.B, 10));
    }

    [Fact]
    public void Response_schema_lists_allowed_moves()
    {
        var turn = new TurnRequest("t", "k", Seat.A, FieldRule.Forbidden, FieldRule.Required, new TemplateRef("i"), new MoveFormat(new TemplateRef("f"), ["split", "steal"]));

        var schema = ResponseSchema.ForTurn(turn).ToJsonString();

        Assert.Contains("\"enum\":[\"split\",\"steal\",null]", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"thought\",\"message\",\"move\"]", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_schema_uses_a_game_fragment_when_given()
    {
        var fragment = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"integer\"}");
        var turn = new TurnRequest("t", "k", Seat.A, FieldRule.Forbidden, FieldRule.Required, new TemplateRef("i"), new MoveFormat(new TemplateRef("f"), null, fragment));

        Assert.Contains("\"move\":{\"type\":\"integer\"}", ResponseSchema.ForTurn(turn).ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protocol_writer_appends_lines_that_survive_reopening()
    {
        var dir = Path.Combine(Path.GetTempPath(), "llm-shootout-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ProtocolWriter(dir))
            {
                await writer.AppendAsync(new MatchAbortedEvent(DateTimeOffset.UnixEpoch, "r", "d"), TestContext.Current.CancellationToken);
            }

            await using (var writer = new ProtocolWriter(dir))
            {
                await writer.AppendAsync(new MatchAbortedEvent(DateTimeOffset.UnixEpoch, "r2", "d2"), TestContext.Current.CancellationToken);
            }

            Assert.Equal(["r", "r2"], ProtocolReader.ReadAll(dir).OfType<MatchAbortedEvent>().Select(e => e.Reason));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class MatchOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Attempts_and_word_limits_must_be_positive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxAttempts = value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxMessageWords = value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxThoughtWords = value });
    }

    [Fact]
    public void Thought_memory_may_be_zero_but_not_negative()
    {
        Assert.Equal(0, new MatchOptions { ThoughtMemory = 0 }.ThoughtMemory);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { ThoughtMemory = -1 });
    }

    [Fact]
    public void Defaults_match_the_specification()
    {
        var options = new MatchOptions();

        Assert.Equal((3, 40, 20, 10, 1m), (options.MaxAttempts, options.MaxThoughtWords, options.MaxMessageWords, options.ThoughtMemory, options.MaxCostUsd));
        Assert.False(options.RevealIdentity);
        Assert.False(options.RedactIdentity);
    }
}

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
