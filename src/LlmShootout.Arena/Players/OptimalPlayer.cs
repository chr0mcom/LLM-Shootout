using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Players;

/// <summary>
/// A player without a model that always plays the best move the oracle of the game names. In a won or drawn position it plays one of
/// the best moves (a tie is decided with its own seeded generator, so a match can be repeated); in a lost position it plays the move
/// that the game documents for that case. It never resigns and never plays an action that the rules forbid. It needs a game that
/// implements <see cref="Games.IGameOracle"/>, and it costs nothing. For turns without a move and for the debriefing it answers like
/// the random player.
/// </summary>
public sealed class OptimalPlayer(long seed) : IPlayer
{
    private readonly SeededRng _rng = new(seed);

    /// <inheritdoc />
    public string Name => "optimal";

    /// <inheritdoc />
    public ModelIdentity? Model => null;

    /// <inheritdoc />
    public bool RequiresOracle => true;

    /// <inheritdoc />
    public Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new PlayerReply((request.BestAnswer ?? request.SampleValidAnswer)(_rng)));
    }
}
