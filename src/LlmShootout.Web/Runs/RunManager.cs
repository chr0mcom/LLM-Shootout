using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Arena.Templates;
using LlmShootout.Games;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Runs;

/// <summary>Starts matches in the background, one at a time, and keeps the latest one for the browser to follow.</summary>
internal sealed class RunManager(IPlayerSource players, UsageLedger usage, IOptions<WebOptions> web, TimeProvider time, ILoggerFactory loggers, IHostApplicationLifetime lifetime) : IHostedService
{
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(8);

    private readonly object _gate = new();
    private readonly ILogger _logger = loggers.CreateLogger<RunManager>();
    private ActiveRun? _current;
    private bool _starting;
    private Task _task = Task.CompletedTask;

    /// <summary>The running match, or the one that ran last.</summary>
    public ActiveRun? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public ActiveRun? Find(string id) => Current is { } run && run.Id == id ? run : null;

    public async Task<ActiveRun> StartMatchAsync(StartRequest request, CancellationToken cancellationToken)
    {
        var (definition, setup, maxCost) = Validate(request);
        Reserve();
        try
        {
            var a = await players.CreateAsync(request.A, setup.Seed + 1, cancellationToken);
            var b = await players.CreateAsync(request.B, setup.Seed + 2, cancellationToken);
            var directory = MatchPaths.Match(web.Value.MatchesDirectory, time.GetUtcNow(), definition.Id, a.Name, b.Name);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            var run = new ActiveRun(Guid.NewGuid().ToString("N")[..12], directory, request, time.GetUtcNow(), cancellation, time);
            var options = new MatchOptions { Debrief = request.Debrief ?? true, MaxCostUsd = maxCost };
            var plan = new MatchPlan(definition, setup, new Dictionary<Seat, IPlayer> { [Seat.A] = new ProgressPlayer(a, Seat.A, run), [Seat.B] = new ProgressPlayer(b, Seat.B, run) }, options, directory);
            lock (_gate)
            {
                _current = run;
            }

            usage.RecordStart();
            _task = Task.Run(() => ExecuteAsync(run, plan, cancellation), CancellationToken.None);
            return run;
        }
        catch (PlayerSpecException ex)
        {
            throw new StartRejectedException(ex.Message);
        }
        catch (Exception ex) when (ex is (HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            throw new StartRejectedException("A model list did not answer in time. Try again in a moment.", StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            lock (_gate)
            {
                _starting = false;
            }
        }
    }

    public bool Cancel(string id)
    {
        var run = Find(id);
        if (run is null || run.IsFinished)
        {
            return false;
        }

        run.Cancel();
        return true;
    }

    private void Reserve()
    {
        lock (_gate)
        {
            if (_starting || _current is { IsFinished: false })
            {
                throw new StartRejectedException("A match is already running. Wait for it to end or cancel it.", StatusCodes.Status409Conflict);
            }

            usage.EnsureAllowed();
            _starting = true;
        }
    }

    private (IGameDefinition Definition, GameSetup Setup, decimal MaxCost) Validate(StartRequest request)
    {
        var definition = GameCatalog.Find(request.Game ?? string.Empty)
            ?? throw new StartRejectedException($"Unknown game '{request.Game}'. Available: {string.Join(", ", GameCatalog.All.Select(g => g.Id))}.");
        var language = (request.Language ?? ArenaTemplates.DefaultLanguage).ToLowerInvariant();
        if (language is not ("de" or "en"))
        {
            throw new StartRejectedException("Language must be 'de' or 'en'.");
        }

        var maxCost = request.MaxCostUsd ?? web.Value.DefaultMaxCostUsd;
        if (maxCost <= 0 || maxCost > web.Value.MaxCostCapUsd)
        {
            throw new StartRejectedException($"The spending limit must be above 0 and at most {web.Value.MaxCostCapUsd} USD.");
        }

        var seed = request.Seed ?? time.GetUtcNow().Ticks % int.MaxValue;
        var firstSeat = string.IsNullOrWhiteSpace(request.FirstSeat)
            ? new SeededRng(seed).NextBool(0.5) ? Seat.A : Seat.B
            : Enum.TryParse<Seat>(request.FirstSeat, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new StartRejectedException("First seat must be A or B.");
        RequireSpec(request.A, "Player A");
        RequireSpec(request.B, "Player B");
        return (definition, new GameSetup(seed, request.Communication ?? true, language, firstSeat, null), maxCost);
    }

    private static void RequireSpec(string? spec, string label)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Length > 200)
        {
            throw new StartRejectedException($"{label} is missing. Pick 'random' or a model.");
        }

        if (spec.Trim().StartsWith("scripted", StringComparison.OrdinalIgnoreCase))
        {
            throw new StartRejectedException("Scripted players read files and are not available here.");
        }
    }

    private async Task ExecuteAsync(ActiveRun run, MatchPlan plan, CancellationTokenSource cancellation)
    {
        var (status, detail) = await PlayAsync(run, plan, cancellation.Token);
        await WriteReportAsync(run, plan);
        RecordCost(run, plan);
        run.Complete(status, detail);
        cancellation.Dispose();
    }

    /// <summary>Plays the match and says how it ended. Never throws: an unexpected exception becomes the status <c>failed</c>.</summary>
    private async Task<(string Status, string? Detail)> PlayAsync(ActiveRun run, MatchPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            var runner = new MatchRunner(new CostMeter(plan.Options.MaxCostUsd), time, loggers.CreateLogger<MatchRunner>(), run.Append);
            var outcome = await runner.RunAsync(plan, cancellationToken);
            return (StatusOf(outcome), outcome.AbortDetail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (RunStatus.Cancelled, "The run was cancelled.");
        }
        catch (Exception ex)
        {
            WebLog.RunFailed(_logger, ex, run.Id);
            return (RunStatus.Failed, "The match failed unexpectedly. The server log has the details.");
        }
    }

    /// <summary>Books the cost of the match against the daily limit. Never throws.</summary>
    private void RecordCost(ActiveRun run, MatchPlan plan)
    {
        try
        {
            usage.RecordCost(MatchRecord.Load(plan.Directory).CostUsd);
        }
        catch (Exception ex)
        {
            WebLog.CostBookingFailed(_logger, ex, run.Id);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Gives a running match a moment to write its closing event and report when the container stops.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task running;
        lock (_gate)
        {
            running = _task;
        }

        await Task.WhenAny(running, Task.Delay(ShutdownGrace, cancellationToken));
    }

    /// <summary>Writes <c>report.md</c> and <c>match.json</c>. Never throws: a match that stopped before it began has no protocol to report on.</summary>
    private async Task WriteReportAsync(ActiveRun run, MatchPlan plan)
    {
        try
        {
            await MatchReportWriter.WriteAsync(plan.Definition, MatchRecord.Load(plan.Directory), CancellationToken.None);
        }
        catch (Exception ex)
        {
            WebLog.ReportFailed(_logger, ex, run.Id);
        }
    }

    private static string StatusOf(MatchOutcome outcome) => outcome.AbortReason switch
    {
        null => RunStatus.Finished,
        AbortReason.Cancelled => RunStatus.Cancelled,
        _ => RunStatus.Aborted,
    };
}
