using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;

namespace LlmShootout.Tests.Protocol;

public sealed class ProtocolVersionTests : IDisposable
{
    private readonly string _directory = TempDirectory.Create("version");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static TurnEvent Turn(int? estimate) =>
        new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), "guess-1", "guess", Seat.A, "10", "played", "thought", false, 1, 0, 0, Estimate: estimate);

    private static string LineWithVersion(string line, int version) =>
        line.Replace($"\"schemaVersion\":{ProtocolVersion.Current}", $"\"schemaVersion\":{version}", StringComparison.Ordinal);

    [Fact]
    public void The_current_version_is_2_and_the_oldest_readable_one_is_1()
    {
        Assert.Equal((2, 1), (ProtocolVersion.Current, ProtocolVersion.Oldest));
    }

    [Fact]
    public void The_estimate_of_a_turn_is_written_as_a_number_and_read_back_in_version_2()
    {
        var line = ProtocolJson.ToLine(Turn(73));

        Assert.Contains("\"estimate\":73", line, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":2", line, StringComparison.Ordinal);
        var read = Assert.IsType<TurnEvent>(ProtocolJson.FromLine(line));
        Assert.Equal((73, 2), (read.Estimate, read.SchemaVersion));
    }

    [Fact]
    public void A_turn_without_an_estimate_writes_no_estimate_key()
    {
        Assert.DoesNotContain("estimate", ProtocolJson.ToLine(Turn(null)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_1_turn_event_reads_as_a_turn_without_an_estimate()
    {
        var line = LineWithVersion(ProtocolJson.ToLine(Turn(null)), 1);

        var read = Assert.IsType<TurnEvent>(ProtocolJson.FromLine(line));

        Assert.Equal((1, (int?)null), (read.SchemaVersion, read.Estimate));
    }

    [Fact]
    public void A_protocol_with_a_newer_version_is_refused_with_an_understandable_message()
    {
        File.WriteAllText(Path.Combine(_directory, ProtocolFiles.Events), LineWithVersion(ProtocolJson.ToLine(Turn(5)), 3) + "\n");

        var failure = Assert.Throws<InvalidDataException>(() => ProtocolReader.ReadAll(_directory));

        Assert.Contains("protocol version 3", failure.Message, StringComparison.Ordinal);
        Assert.Contains("versions up to 2", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_without_a_version_counts_as_the_oldest_one()
    {
        var line = ProtocolJson.ToLine(Turn(null)).Replace($",\"schemaVersion\":{ProtocolVersion.Current}", string.Empty, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_directory, ProtocolFiles.Events), line + "\n");

        var read = Assert.IsType<TurnEvent>(Assert.Single(ProtocolReader.ReadAll(_directory)));

        Assert.Equal("guess-1", read.TurnId);
        Assert.Equal(ProtocolVersion.Oldest, read.SchemaVersion);
    }

    [Fact]
    public void A_protocol_without_any_version_is_summarized_like_a_version_1_protocol()
    {
        var fixture = File.ReadAllText(Path.Combine(Snapshot.RepositoryRoot, "tests", "LlmShootout.Tests", "Protocol", "Legacy", "events-v1.jsonl"));
        File.WriteAllText(Path.Combine(_directory, ProtocolFiles.Events), fixture.Replace(",\"schemaVersion\":1", string.Empty, StringComparison.Ordinal));

        var match = MatchRecord.Load(_directory);

        Assert.All(match.Events, e => Assert.Equal(1, e.SchemaVersion));
        Assert.False(PlayMetrics.RecordsMoveKinds(match));
        var summary = MatchReportWriter.Summarize(match);
        Assert.All(summary.Seats.Values, seat => Assert.Null(seat.RuleViolations));
    }

    [Theory]
    [InlineData("0", "invalid protocol version 0")]
    [InlineData("-1", "invalid protocol version -1")]
    [InlineData("\"2\"", "not a whole number")]
    public void A_version_below_1_or_not_a_number_is_refused(string version, string expected)
    {
        var line = ProtocolJson.ToLine(Turn(null)).Replace($"\"schemaVersion\":{ProtocolVersion.Current}", $"\"schemaVersion\":{version}", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_directory, ProtocolFiles.Events), line + "\n");

        var failure = Assert.Throws<InvalidDataException>(() => ProtocolReader.ReadAll(_directory));

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }
}
