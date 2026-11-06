using LlmShootout.Arena.Games;

namespace LlmShootout.Arena;

/// <summary>A finished or aborted match as read back from its protocol.</summary>
/// <param name="Directory">Directory of the match.</param>
/// <param name="GameId">Id of the game.</param>
/// <param name="Communication">True if the players could talk.</param>
/// <param name="Result">The result, or null if the match was aborted.</param>
public sealed record MatchRecord(string Directory, string GameId, bool Communication, GameResult? Result);
