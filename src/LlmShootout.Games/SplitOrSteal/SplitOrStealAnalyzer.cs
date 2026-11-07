using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>Report sections of Split or Steal: none beyond the general report.</summary>
public sealed class SplitOrStealAnalyzer : IGameAnalyzer
{
    /// <inheritdoc />
    public IReadOnlyList<ReportSection> AnalyzeMatch(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return [];
    }
}
