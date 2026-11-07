using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests;

public sealed class MatchHarness : IDisposable
{
    private readonly string _root = TempDirectory.Create("tests");

    public MatchHarness(decimal maxCostUsd = 100m)
    {
        Cost = new CostMeter(maxCostUsd);
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    }

    public CostMeter Cost { get; }

    public FakeTimeProvider Time { get; }

    public string Directory => _root;

    public static string Answer(string? thought = "t", string? message = null, string? move = null) =>
        new JsonObject { ["thought"] = thought, ["message"] = message, ["move"] = move }.ToJsonString();

    public static ModelCallInfo Info(decimal? cost = 0.001m, string model = "test/model") =>
        new(model, model, "test", "gen-1", 10, 5, null, cost, TimeSpan.FromSeconds(1), "stop", 1);

    public async Task<(MatchOutcome Outcome, IReadOnlyList<MatchEvent> Events)> RunAsync(
        IGameDefinition definition,
        IPlayer a,
        IPlayer b,
        bool communication = false,
        MatchOptions? options = null,
        long seed = 7,
        JsonObject? gameOptions = null,
        Seat firstSeat = Seat.A,
        bool debrief = false) =>
        await RunWithTokenAsync(definition, a, b, communication, (options ?? new MatchOptions()) with { Debrief = debrief }, seed, gameOptions, firstSeat, TestContext.Current.CancellationToken);

    public async Task<(MatchOutcome Outcome, IReadOnlyList<MatchEvent> Events)> RunWithTokenAsync(
        IGameDefinition definition,
        IPlayer a,
        IPlayer b,
        bool communication,
        MatchOptions? options,
        long seed,
        JsonObject? gameOptions,
        Seat firstSeat,
        CancellationToken cancellationToken,
        string language = "en")
    {
        var plan = new MatchPlan(
            definition,
            new GameSetup(seed, communication, language, firstSeat, gameOptions),
            new Dictionary<Seat, IPlayer> { [Seat.A] = a, [Seat.B] = b },
            options ?? new MatchOptions(),
            Path.Combine(_root, Guid.NewGuid().ToString("N")));
        var outcome = await new MatchRunner(Cost, Time).RunAsync(plan, cancellationToken);
        return (outcome, ProtocolReader.ReadAll(plan.Directory));
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
        {
            System.IO.Directory.Delete(_root, true);
        }
    }
}
