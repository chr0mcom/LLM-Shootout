using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Tests.TestGames;
using LlmShootout.Web.Matches;

namespace LlmShootout.Tests.Protocol;

public sealed class LegacyProtocolTests : IDisposable
{
    private readonly string _directory = TempDirectory.Create("legacy");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private MatchRecord LoadVersion1()
    {
        File.Copy(Path.Combine(Snapshot.RepositoryRoot, "tests", "LlmShootout.Tests", "Protocol", "Legacy", "events-v1.jsonl"), Path.Combine(_directory, ProtocolFiles.Events));
        return MatchRecord.Load(_directory);
    }

    [Fact]
    public void A_protocol_with_an_option_that_no_longer_exists_still_loads()
    {
        var match = LoadVersion1();

        Assert.Contains("removedSetting", File.ReadAllText(Path.Combine(_directory, ProtocolFiles.Events)), StringComparison.Ordinal);
        Assert.Equal(1, match.Started.SchemaVersion);
        Assert.Equal(3, match.Started.Options.MaxAttempts);
        Assert.NotNull(match.Result);
    }

    [Fact]
    public void The_turns_of_a_version_1_protocol_have_no_estimate()
    {
        var match = LoadVersion1();

        Assert.NotEmpty(match.Turns());
        Assert.All(match.Turns(), turn => Assert.Null(turn.Estimate));
        Assert.All(match.Turns(), turn => Assert.Null(turn.MoveKind));
        Assert.All(match.Events, e => Assert.Equal(1, e.SchemaVersion));
    }

    [Fact]
    public void The_summary_and_the_report_of_an_old_protocol_can_be_built()
    {
        var match = LoadVersion1();

        var summary = MatchReportWriter.Summarize(match);
        var report = MatchReportWriter.BuildReport(new CodeBreakerDefinition(), match);

        Assert.Equal("code-breaker", summary.GameId);
        Assert.Contains("Code breaker", report, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rule_compliance_section_of_an_old_protocol_shows_only_what_the_protocol_records()
    {
        var match = LoadVersion1();

        var report = MatchReportWriter.BuildReport(new CodeBreakerDefinition(), match);
        var summary = MatchReportWriter.Summarize(match);

        Assert.Contains("## Rule compliance", report, StringComparison.Ordinal);
        Assert.Contains("| Rejected answers: invalid |", report, StringComparison.Ordinal);
        Assert.Contains("| Exhausted turns (no acceptable move in any attempt) |", report, StringComparison.Ordinal);
        Assert.DoesNotContain("| Rule violations |", report, StringComparison.Ordinal);
        Assert.DoesNotContain("| Resignations |", report, StringComparison.Ordinal);
        Assert.DoesNotContain("No rule violations were recorded", report, StringComparison.Ordinal);
        Assert.Null(summary.ResultWithRuleViolation);
    }

    [Fact]
    public void The_summary_of_an_old_protocol_has_no_figure_for_what_it_does_not_record()
    {
        var match = LoadVersion1();

        var summary = MatchReportWriter.Summarize(match);
        var json = SummaryJson.Write(summary);

        Assert.All(summary.Seats.Values, seat => Assert.Equal((null, null), (seat.RuleViolations, seat.Resignations)));
        Assert.Contains("\"ruleViolations\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"resignations\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"resultWithRuleViolation\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"exhaustedTurns\": 0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overview_sheet_of_an_old_protocol_shows_n_a_instead_of_zero()
    {
        var overview = MatchExport.Overview(LoadVersion1());

        string? Cell(string item) => overview.Rows.Single(row => Equals(row[0], item))[1]?.ToString();
        Assert.Equal("n/a", Cell("Result with rule violation"));
        Assert.Equal("n/a", Cell("Seat A: rule violations"));
        Assert.Equal("n/a", Cell("Seat B: resignations"));
        Assert.Equal("0", Cell("Seat A: exhausted turns"));
    }
}
