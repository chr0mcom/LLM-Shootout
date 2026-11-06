using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Players;

/// <summary>A player that answers with random legal answers. For demos and tests without an API key.</summary>
public sealed class RandomPlayer(long seed) : IPlayer
{
    private readonly SeededRng _rng = new(seed);

    /// <inheritdoc />
    public string Name => "random";

    /// <inheritdoc />
    public ModelIdentity? Model => null;

    /// <inheritdoc />
    public Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new PlayerReply(request.SampleValidAnswer(_rng)));
    }
}
