using LlmShootout.Arena.Games;
using LlmShootout.Games.Nim;
using LlmShootout.Games.SplitOrSteal;

namespace LlmShootout.Games;

/// <summary>
/// The games of the application, registered explicitly. To add a game, add one line here; nothing is discovered by reflection.
/// </summary>
public static class GameCatalog
{
    /// <summary>All games, in the order of the article series.</summary>
    public static IReadOnlyList<IGameDefinition> All { get; } = [new NimDefinition(), new SplitOrStealDefinition()];

    /// <summary>Finds a game by id, or null.</summary>
    public static IGameDefinition? Find(string id) =>
        All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
}
