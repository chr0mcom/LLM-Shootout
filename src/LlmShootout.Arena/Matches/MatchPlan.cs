using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;

namespace LlmShootout.Arena.Matches;

/// <summary>Everything needed to run one match.</summary>
/// <param name="Definition">The game.</param>
/// <param name="Setup">Seed, communication, language, options.</param>
/// <param name="Players">One player per seat.</param>
/// <param name="Options">Settings independent of the game.</param>
/// <param name="Directory">Where the protocol is written.</param>
public sealed record MatchPlan(
    IGameDefinition Definition,
    GameSetup Setup,
    IReadOnlyDictionary<Seat, IPlayer> Players,
    MatchOptions Options,
    string Directory);

/// <summary>How a match ended.</summary>
/// <param name="Directory">The match directory.</param>
/// <param name="Result">The game result, or null if the match was aborted.</param>
/// <param name="AbortReason">Machine-readable abort reason, or null.</param>
/// <param name="AbortDetail">Human readable abort detail, or null.</param>
public sealed record MatchOutcome(string Directory, GameResult? Result, string? AbortReason, string? AbortDetail)
{
    /// <summary>True if the game ran to its end.</summary>
    public bool Completed => Result is not null;
}
