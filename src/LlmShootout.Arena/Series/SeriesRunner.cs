using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Reports;

namespace LlmShootout.Arena.Series;

/// <summary>Everything needed to run a series.</summary>
/// <param name="Definition">The game.</param>
/// <param name="File">The series file.</param>
/// <param name="PlayerA">Player for seat A.</param>
/// <param name="PlayerB">Player for seat B.</param>
/// <param name="Language">Prompt language.</param>
/// <param name="Options">Match settings.</param>
/// <param name="Directory">The series directory.</param>
public sealed record SeriesPlan(
    IGameDefinition Definition,
    SeriesFile File,
    IPlayer PlayerA,
    IPlayer PlayerB,
    string Language,
    MatchOptions Options,
    string Directory);

/// <summary>Runs the matches of a series one after the other and writes <c>series.json</c> and <c>series-report.md</c>.</summary>
public sealed class SeriesRunner(MatchRunner runner, CostMeter cost)
{
    internal const int RandomSeedOffsetA = 1000;
    internal const int RandomSeedOffsetB = 2000;

    /// <summary>Called after each finished match, for progress output.</summary>
    public Action<int, MatchOutcome>? OnMatchFinished { get; set; }

    /// <summary>Plays the series. Stops early if the cost limit is reached.</summary>
    public async Task<SeriesSummary> RunAsync(SeriesPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateGameOptions(plan);
        var records = new List<MatchRecord>();
        var number = 0;
        foreach (var match in plan.File.Matches)
        {
            number++;
            if (number > 1 && cost.IsExhausted)
            {
                break;
            }

            var matchPlan = PlanMatch(plan, match, number);

            var outcome = await runner.RunAsync(matchPlan, cancellationToken);
            var record = MatchRecord.Load(outcome.Directory);
            await MatchReportWriter.WriteAsync(plan.Definition, record, cancellationToken);
            records.Add(record);
            OnMatchFinished?.Invoke(number, outcome);
            if (outcome.AbortReason == AbortReason.CostLimit)
            {
                break;
            }
        }

        var summary = SeriesReportWriter.Summarize(plan.Definition, plan.PlayerA.Name, plan.PlayerB.Name, records, plan.Language);
        await SeriesReportWriter.WriteAsync(plan.Definition, plan.Directory, summary, plan.Language, cancellationToken);
        return summary;
    }

    private static MatchPlan PlanMatch(SeriesPlan plan, SeriesMatch match, int number)
    {
        var seed = plan.File.Seed + number;
        var first = match.FirstSeat ?? (new SeededRng(seed).NextBool(0.5) ? Seat.A : Seat.B);
        return new MatchPlan(
            plan.Definition,
            new GameSetup(seed, plan.Definition.ResolveCommunication(match.Communication), plan.Language, first, match.Options),
            new Dictionary<Seat, IPlayer> { [Seat.A] = Fresh(plan.PlayerA, seed + RandomSeedOffsetA), [Seat.B] = Fresh(plan.PlayerB, seed + RandomSeedOffsetB) },
            plan.Options,
            MatchPaths.SeriesMatch(plan.Directory, number, first));
    }

    /// <summary>Random players are created anew for every match from the match seed, so a match does not depend on the matches before it.</summary>
    private static IPlayer Fresh(IPlayer player, long seed) => player is RandomPlayer ? new RandomPlayer(seed) : player;

    /// <summary>Creates every game once, so that wrong options are found before the first paid match.</summary>
    private static void ValidateGameOptions(SeriesPlan plan)
    {
        var number = 0;
        foreach (var match in plan.File.Matches)
        {
            number++;
            try
            {
                plan.Definition.Create(PlanMatch(plan, match, number).Setup);
            }
            catch (GameOptionsException ex)
            {
                throw new GameOptionsException($"Match {number} of the series: {ex.Message}", ex);
            }
        }
    }
}
