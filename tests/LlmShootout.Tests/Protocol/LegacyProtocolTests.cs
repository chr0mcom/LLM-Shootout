using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Games.SplitOrSteal;

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
    public void A_protocol_written_before_the_identity_options_were_removed_still_loads()
    {
        var match = LoadVersion1();

        Assert.Equal(1, match.Started.SchemaVersion);
        Assert.Equal(3, match.Started.Options.MaxAttempts);
        Assert.NotNull(match.Result);
    }

    [Fact]
    public void The_summary_and_the_report_of_an_old_protocol_can_be_built()
    {
        var match = LoadVersion1();

        var summary = MatchReportWriter.Summarize(match);
        var report = MatchReportWriter.BuildReport(new SplitOrStealDefinition(), match);

        Assert.Equal("split-or-steal", summary.GameId);
        Assert.Contains("Split or Steal", report, StringComparison.Ordinal);
    }
}
