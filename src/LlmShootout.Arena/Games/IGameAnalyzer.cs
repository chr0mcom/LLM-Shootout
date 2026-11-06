namespace LlmShootout.Arena.Games;

/// <summary>A named block of Markdown in a report.</summary>
public sealed record ReportSection(string Title, string Markdown);

/// <summary>
/// Game specific analysis for reports. Works only on the protocol events, so reports can be regenerated
/// without running the game again.
/// </summary>
public interface IGameAnalyzer
{
    /// <summary>Sections for the report of one match.</summary>
    IReadOnlyList<ReportSection> AnalyzeMatch(MatchRecord match);

    /// <summary>Sections for the report of a series, for example the comparison with and without communication.</summary>
    IReadOnlyList<ReportSection> AnalyzeSeries(IReadOnlyList<MatchRecord> matches);
}
