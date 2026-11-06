using LlmShootout.Arena.Players;

namespace LlmShootout.Arena.Matches;

/// <summary>
/// The single door through which every call to a player passes (turns, debriefing, judge): checks the spending
/// limit before the call and books the cost after it. A failed call to a model counts as "cost unknown",
/// because a cancelled request may still be billed.
/// </summary>
public sealed class PlayerGateway(CostMeter cost)
{
    /// <summary>Calls a player.</summary>
    public async Task<PlayerReply> CallAsync(IPlayer player, PlayerCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        cost.EnsureWithinLimit();
        try
        {
            var reply = await player.RespondAsync(request, cancellationToken);
            if (reply.Call is not null)
            {
                cost.Record(reply.Call.CostUsd);
            }

            return reply;
        }
        catch (PlayerFailureException)
        {
            if (player.Model is not null)
            {
                cost.Record(null);
            }

            throw;
        }
    }
}
