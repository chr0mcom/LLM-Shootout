using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;

namespace LlmShootout.Web.Runs;

/// <summary>Wraps a player and tells the run when it starts and stops answering, so that the page can show who is being waited for.</summary>
internal sealed class ProgressPlayer(IPlayer inner, Seat seat, ActiveRun run) : IPlayer
{
    public string Name => inner.Name;

    public ModelIdentity? Model => inner.Model;

    public IReadOnlyDictionary<string, string> Settings => inner.Settings;

    public async Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
    {
        run.SetBusy(seat, busy: true);
        try
        {
            return await inner.RespondAsync(request, cancellationToken);
        }
        finally
        {
            run.SetBusy(seat, busy: false);
        }
    }
}
