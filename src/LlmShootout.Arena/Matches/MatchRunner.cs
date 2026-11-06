using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmShootout.Arena.Matches;

/// <summary>
/// Runs one match from the first turn to the result and writes the protocol. The loop is the same for every game:
/// ask the pending seats, collect their answers, hand the whole round to the game, record what happened.
/// </summary>
public sealed class MatchRunner(CostMeter cost, TimeProvider time, ILogger<MatchRunner>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<MatchRunner>.Instance;

    /// <summary>Plays the match. A technical failure or the cost limit abort it cleanly; the protocol stays valid.</summary>
    public async Task<MatchOutcome> RunAsync(MatchPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var game = plan.Definition.Create(plan.Setup);
        var prompts = new PromptBuilder(ArenaTemplates.Default, plan.Definition.Templates, plan.Setup.Language, plan.Options, plan.Setup.Communication);
        var evaluator = new AnswerEvaluator(plan.Options, prompts);

        await using var writer = new ProtocolWriter(plan.Directory);
        var guard = new IdentityGuard(seat => plan.Players[seat].Model, plan.Options.IdentityScanner, plan.Options.RedactIdentity);
        var context = new MatchContext(plan, game, prompts, evaluator, writer, cost, new PlayerGateway(cost), guard, time);
        var (startCost, startUnknown) = (cost.TotalUsd, cost.UnknownCalls);
        GameResult? finished = null;
        MatchLog.Started(_logger, plan.Definition.Id, plan.Setup.Seed, plan.Directory);
        await writer.AppendAsync(StartedEvent(plan), cancellationToken);

        try
        {
            var executor = new TurnExecutor(context, _logger);
            while (game.PendingTurns.Count > 0)
            {
                await PlayRoundAsync(context, executor, cancellationToken);
            }

            var result = game.Result ?? throw new InvalidOperationException($"Game '{plan.Definition.Id}' ended without a result.");
            finished = result;
            await writer.AppendAsync(new MatchFinishedEvent(time.GetUtcNow(), result, cost.TotalUsd - startCost, cost.UnknownCalls - startUnknown), cancellationToken);
            MatchLog.Finished(_logger, result.Outcome);
            if (plan.Options.Debrief)
            {
                await new Debriefer(context.Gateway, time).RunAsync(plan.Definition, MatchRecord.Load(plan.Directory), plan.Players, plan.Options, writer, cancellationToken);
            }

            return new MatchOutcome(plan.Directory, result, null, null);
        }
        catch (BudgetExceededException ex)
        {
            return await AbortAsync(context, "cost_limit", ex.Message, finished);
        }
        catch (PlayerFailureException ex)
        {
            return await AbortAsync(context, ex.IsTimeout ? "timeout" : "player_failure", ex.Message, finished);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbortAsync(context, "cancelled", "The run was cancelled.", finished);
            throw;
        }
    }

    private async Task PlayRoundAsync(MatchContext context, TurnExecutor executor, CancellationToken cancellationToken)
    {
        var turns = context.Game.PendingTurns.ToArray();
        var results = new List<TurnResult>(turns.Length);
        foreach (var turn in turns)
        {
            results.Add(await executor.ExecuteAsync(turn, cancellationToken));
        }

        foreach (var (turn, result) in turns.Zip(results))
        {
            await RecordTurnAsync(context, turn, result, cancellationToken);
        }

        var resolution = context.Game.Apply([.. results.Select(r => r.Submission)]);
        foreach (var fact in resolution.Facts)
        {
            var turnId = turns[0].TurnId;
            context.Timeline.AddFact(turnId, fact);
            await context.Writer.AppendAsync(
                new FactEvent(time.GetUtcNow(), turnId, fact.Kind, fact.Audience, fact.Seat, context.Prompts.RenderFact(fact, null), fact.Description, fact.Data),
                cancellationToken);
        }
    }

    private async Task RecordTurnAsync(MatchContext context, TurnRequest turn, TurnResult result, CancellationToken cancellationToken)
    {
        await context.Writer.AppendAsync(
            new TurnEvent(
                time.GetUtcNow(),
                turn.TurnId,
                turn.Kind,
                turn.Seat,
                result.Submission.Move,
                result.Submission.Status.ToString().ToLowerInvariant(),
                result.Thought,
                result.ThoughtTruncated,
                result.Attempts,
                result.InvalidAnswers,
                result.Refusals,
                result.TokenLimitHits),
            cancellationToken);

        if (result.MessageAllowed)
        {
            var inspected = context.IdentityGuard.Inspect(turn.Seat, result.Message);
            context.Timeline.AddMessage(turn.TurnId, turn.Seat, inspected.Text);
            await context.Writer.AppendAsync(
                new MessageEvent(time.GetUtcNow(), turn.TurnId, turn.Seat, inspected.Text, result.Silent, result.MessageTruncated, inspected.Hits),
                cancellationToken);
        }
    }

    private async Task<MatchOutcome> AbortAsync(MatchContext context, string reason, string detail, GameResult? finished)
    {
        MatchLog.Aborted(_logger, reason, detail);
        await context.Writer.AppendAsync(new MatchAbortedEvent(time.GetUtcNow(), reason, detail), CancellationToken.None);
        return new MatchOutcome(context.Plan.Directory, finished, reason, detail);
    }

    private MatchStartedEvent StartedEvent(MatchPlan plan)
    {
        var players = Seat.All.ToDictionary(
            seat => seat,
            seat => new PlayerDescription(plan.Players[seat].Name, plan.Players[seat].Model?.Slug));
        var language = plan.Setup.Language;
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["arena"] = ArenaTemplates.Default.Hash(language),
            [plan.Definition.Id] = plan.Definition.Templates.Hash(language),
        };
        var (version, commit) = AppInfo.Current;
        return new MatchStartedEvent(
            time.GetUtcNow(),
            plan.Definition.Id,
            plan.Setup.Seed,
            language,
            plan.Setup.Communication,
            plan.Setup.FirstSeat,
            plan.Setup.Options,
            players,
            hashes,
            plan.Options,
            version,
            commit);
    }
}
