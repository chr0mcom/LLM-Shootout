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
/// <param name="Options">Match settings; the reveal setting is taken from each match of the series file.</param>
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
public sealed class SeriesRunner(MatchRunner runner)
{
    /// <summary>Called after each finished match, for progress output.</summary>
    public Action<int, MatchOutcome>? OnMatchFinished { get; set; }

    /// <summary>Plays the series. Stops early if the cost limit is reached.</summary>
    public async Task<SeriesSummary> RunAsync(SeriesPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var records = new List<MatchRecord>();
        var entries = new List<SeriesEntry>();
        var number = 0;
        foreach (var match in plan.File.Matches)
        {
            number++;
            var seed = plan.File.Seed + number;
            var first = match.FirstSeat ?? (new SeededRng(seed).NextBool(0.5) ? Seat.A : Seat.B);
            var matchPlan = new MatchPlan(
                plan.Definition,
                new GameSetup(seed, match.Communication, plan.Language, first, match.Options),
                new Dictionary<Seat, IPlayer> { [Seat.A] = plan.PlayerA, [Seat.B] = plan.PlayerB },
                plan.Options with { RevealIdentity = match.RevealIdentity },
                MatchPaths.SeriesMatch(plan.Directory, number, match.Communication, first));

            var outcome = await runner.RunAsync(matchPlan, cancellationToken);
            var record = MatchRecord.Load(outcome.Directory);
            await MatchReportWriter.WriteAsync(plan.Definition, record, cancellationToken);
            records.Add(record);
            entries.Add(new SeriesEntry(Path.GetFileName(outcome.Directory), match.Communication, record.Result?.Outcome, record.CostUsd));
            OnMatchFinished?.Invoke(number, outcome);
            if (outcome.AbortReason == "cost_limit")
            {
                break;
            }
        }

        var summary = Summarize(plan, records, entries);
        await SeriesReportWriter.WriteAsync(plan.Definition, plan.Directory, summary, records, plan.Language, cancellationToken);
        return summary;
    }

    private static SeriesSummary Summarize(SeriesPlan plan, List<MatchRecord> records, List<SeriesEntry> entries) =>
        new(
            plan.Definition.Id,
            plan.PlayerA.Name,
            plan.PlayerB.Name,
            entries.Count,
            records.Count(r => r.Result is not null),
            records.Count(r => r.Aborted is not null),
            records.Sum(r => r.CostUsd),
            records.Sum(r => r.CostUnknownCalls),
            Seat.All.ToDictionary(seat => seat, seat => records.Sum(r => r.Result?.Seats[seat].Score ?? 0)),
            entries);
}
