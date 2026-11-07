using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Matches;

/// <summary>Checks a match plan against the game that it creates, before anything is written or paid for.</summary>
public static class MatchPreflight
{
    /// <summary>
    /// Refuses a plan in which a player needs an oracle (the player type <c>optimal</c>) and the game has none.
    /// Throws <see cref="GameOptionsException"/> with a message for the person who started the match.
    /// </summary>
    public static void Ensure(MatchPlan plan, IGame game)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(game);
        if (game is IGameOracle)
        {
            return;
        }

        if (plan.Players.Values.FirstOrDefault(player => player.RequiresOracle) is { } needy)
        {
            throw new GameOptionsException($"Player '{needy.Name}' needs a game that can name the best move; '{plan.Definition.Id}' cannot.");
        }
    }
}
