using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests.Matches;

public sealed class CostTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Cost_meter_sums_known_costs_counts_unknown_and_enforces_the_limit()
    {
        var meter = new CostMeter(0.05m);
        meter.Record(0.02m);
        meter.Record(null);
        meter.EnsureWithinLimit();
        meter.Record(0.03m);

        Assert.Equal(0.05m, meter.TotalUsd);
        Assert.Equal(1, meter.UnknownCalls);
        var ex = Assert.Throws<BudgetExceededException>(meter.EnsureWithinLimit);
        Assert.Contains("limit 0.05", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gateway_books_unknown_cost_for_failed_model_calls_only()
    {
        var cost = new CostMeter(10m);
        var gateway = new PlayerGateway(cost);
        var request = new PlayerCall(CallPurpose.Turn, [], null, 1, _ => "{}");
        var model = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y")).Timeout();
        var plain = new ScriptedPlayer("p").Timeout();

        await Assert.ThrowsAsync<PlayerFailureException>(() => gateway.CallAsync(plain, request, TestContext.Current.CancellationToken));
        Assert.Equal(0, cost.UnknownCalls);
        await Assert.ThrowsAsync<PlayerFailureException>(() => gateway.CallAsync(model, request, TestContext.Current.CancellationToken));
        Assert.Equal(1, cost.UnknownCalls);
    }

    [Fact]
    public async Task The_cost_of_a_failed_call_is_booked_when_the_error_response_reports_it()
    {
        var cost = new CostMeter(10m);
        var player = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y")).Fail("HTTP 500", MatchHarness.Info(0.5m), "raw");

        await Assert.ThrowsAsync<PlayerFailureException>(() => new PlayerGateway(cost).CallAsync(player, new PlayerCall(CallPurpose.Turn, [], null, 1, _ => "{}"), TestContext.Current.CancellationToken));

        Assert.Equal(0.5m, cost.TotalUsd);
        Assert.Equal(0, cost.UnknownCalls);
    }

    [Fact]
    public async Task Estimated_cost_appears_in_the_record_the_summary_and_the_report()
    {
        var definition = new LlmShootout.Games.SplitOrSteal.SplitOrStealDefinition();
        var estimated = MatchHarness.Info(null) with { EstimatedCostUsd = 0.2m };
        var a = new ScriptedPlayer("a", new LlmShootout.Arena.Identity.ModelIdentity("x/a")).Reply(MatchHarness.Answer(move: "split"), info: estimated);
        var b = new ScriptedPlayer("b", new LlmShootout.Arena.Identity.ModelIdentity("x/b")).Reply(MatchHarness.Answer(move: "split"), info: estimated);

        var (outcome, _) = await _harness.RunAsync(definition, a, b, false);

        var record = MatchRecord.Load(outcome.Directory);
        Assert.Equal(0.4m, record.CostUsd);
        Assert.Equal(2, record.CostEstimatedCalls);
        Assert.Equal(0, record.CostUnknownCalls);
        Assert.Equal(2, LlmShootout.Arena.Reports.MatchReportWriter.Summarize(record).CostEstimatedCalls);
        Assert.Contains("estimated from tokens and model price", LlmShootout.Arena.Reports.MatchReportWriter.BuildReport(definition, record), StringComparison.Ordinal);
    }
}
