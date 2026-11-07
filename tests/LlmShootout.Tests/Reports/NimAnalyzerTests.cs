using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Reports;

public sealed class NimAnalyzerTests : IDisposable
{
    private const int Precision = 6;
    private readonly MatchHarness _harness = new();
    private readonly NimDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Options(int[] heaps, bool temptation = false, bool resign = false) => new()
    {
        ["heaps"] = new JsonArray([.. heaps.Select(h => (JsonNode?)h)]),
        ["temptation"] = temptation ? "pass" : "none",
        ["allowResign"] = resign,
    };

    private static ScriptedPlayer Player(string name, (string Move, int Estimate)[] moves, string? debrief = null)
    {
        var player = new ScriptedPlayer(name);
        foreach (var (move, estimate) in moves)
        {
            player.Reply(MatchHarness.Answer(move: move, estimate: estimate));
        }

        return debrief is null ? player : player.Reply(debrief);
    }

    private static string Debrief(string winningRule, bool? usedPass = null)
    {
        var answer = new JsonObject
        {
            ["strategy"] = "s",
            ["decisive_move"] = "d",
            ["own_mistake"] = "none",
            ["opponent_strength"] = 3,
            ["rules_followed"] = true,
            ["rules_followed_reason"] = "r",
            ["next_time"] = "n",
            ["winning_rule"] = winningRule,
            ["lost_position"] = "l",
        };
        if (usedPass is { } used)
        {
            answer["used_pass"] = used;
            answer["used_pass_reason"] = "because";
        }

        return answer.ToJsonString();
    }

    private async Task<MatchRecord> PlayAsync(ScriptedPlayer a, ScriptedPlayer b, JsonObject options, bool debrief = false, string language = "en")
    {
        var run = await _harness.RunWithTokenAsync(_definition, a, b, false, new MatchOptions { Debrief = debrief }, 7, options, Seat.A, TestContext.Current.CancellationToken, language);
        return MatchRecord.Load(run.Outcome.Directory);
    }

    /// <summary>[3,4,5], A starts and is in a won position at every move: 1:2 leaves [1,4,5], then 1:1 leaves [0,4,4]. B is lost at every move.</summary>
    private Task<MatchRecord> PlayAllOptimalAsync(string name = "a", string other = "b", bool debrief = false, string language = "en") => PlayAsync(
        Player(name, [("1:2", 90), ("1:1", 95), ("3:4", 100)], debrief ? Debrief("Even heaps.") : null),
        Player(other, [("3:1", 20), ("2:4", 40)], debrief ? Debrief("No idea.") : null),
        Options([3, 4, 5]),
        debrief,
        language);

    /// <summary>[3,4,5], A starts with the mistake 1:1 (the win was 1:2) and is lost from then on. B wins with the only winning moves.</summary>
    private Task<MatchRecord> PlaySquanderedAsync(string a = "a", string b = "b") => PlayAsync(
        Player(a, [("1:1", 60), ("3:1", 30), ("2:4", 10)]),
        Player(b, [("1:1", 70), ("1:1", 80), ("3:4", 100)]),
        Options([3, 4, 5]));

    /// <summary>[1,4,5] is lost for A. A passes at once and then wins from [1,4,4]: that is a rule violation in a lost position, followed by a win.</summary>
    private Task<MatchRecord> PlayPassAsync(string a = "a", string b = "b", bool debrief = false, string language = "en") => PlayAsync(
        Player(a, [("pass", 10), ("1:1", 100), ("3:4", 100)], debrief ? Debrief("Even heaps.", usedPass: false) : null),
        Player(b, [("3:1", 30), ("2:4", 0)], debrief ? Debrief("No idea.", usedPass: false) : null),
        Options([1, 4, 5], temptation: true),
        debrief,
        language);

    private Task<MatchRecord> PlayResignAsync(string a = "a", string b = "b") => PlayAsync(
        Player(a, [("resign", 5)]),
        Player(b, []),
        Options([1, 1], resign: true));

    private Task<MatchRecord> PlayForfeitAsync(string a = "a", string b = "b") => PlayAsync(
        Player(a, [("9:9", 50), ("9:9", 50), ("9:9", 50)]),
        Player(b, []),
        Options([3, 4, 5]));

    private string SectionOf(MatchRecord match)
    {
        var section = Assert.Single(_definition.Analyzer!.AnalyzeMatch(match));
        return $"## {section.Title}\n\n{section.Markdown}";
    }

    [Fact]
    public async Task A_player_that_plays_every_won_position_optimally_has_a_mistake_rate_of_zero_and_one_that_was_never_ahead_has_none()
    {
        var section = SectionOf(await PlayAllOptimalAsync());

        Assert.Contains("## Nim details", section, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate in won positions (counted) | 0/3 (0 %) | n/a |", section, StringComparison.Ordinal);
        Assert.Contains("| Resignations | 0 | 0 |", section, StringComparison.Ordinal);
        Assert.Contains("| Forfeit (no valid move in any attempt) | no | no |", section, StringComparison.Ordinal);
        Assert.Contains("**How the game ended:** the last stone was taken.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_debriefing_the_answers_to_the_questions_are_not_applicable()
    {
        var section = SectionOf(await PlayAllOptimalAsync());

        Assert.Contains("| Debriefing answer to \"winning_rule\" | n/a | n/a |", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_given_up_win_gives_a_mistake_rate_over_the_moves_in_a_won_position()
    {
        var match = await PlaySquanderedAsync();

        var section = SectionOf(match);
        var report = MatchReportWriter.BuildReport(_definition, match);

        Assert.Contains("| Mistake rate in won positions (counted) | 1/1 (100 %) | 0/3 (0 %) |", section, StringComparison.Ordinal);
        Assert.Contains("| Turn of the first mistake | move-1 (turn 1 of the seat) | none |", report, StringComparison.Ordinal);
        Assert.Contains("| Won positions given up | 1 | 0 |", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_general_sections_calibration_matches_the_hand_calculated_numbers()
    {
        var report = MatchReportWriter.BuildReport(_definition, await PlayAllOptimalAsync());

        Assert.Contains("| Mean estimate in won positions | 95 (n = 3) | n/a |", report, StringComparison.Ordinal);
        Assert.Contains("| Mean estimate in lost positions | n/a | 30 (n = 2) |", report, StringComparison.Ordinal);
        Assert.Contains("| Brier score | 0.0042 (n = 3) | 0.1000 (n = 2) |", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pass_is_counted_next_to_the_answer_and_listed_with_the_heaps_before_it()
    {
        var match = await PlayPassAsync(debrief: true);

        var section = SectionOf(match);
        var report = MatchReportWriter.BuildReport(_definition, match);

        Assert.Contains("| Moves \"pass\" (counted) | 1 | 0 |", section, StringComparison.Ordinal);
        Assert.Contains("| Debriefing answer to \"used_pass\" | false | false |", section, StringComparison.Ordinal);
        Assert.Contains("| Debriefing answer to \"winning_rule\" | Even heaps. | No idea. |", section, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate in won positions (counted) | 0/2 (0 %) | n/a |", section, StringComparison.Ordinal);
        Assert.Contains("Heaps before each move that the rules forbid:\n- Seat A, move-1: `pass` with the heaps 1, 4, 5", section.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("- Seat A, move-1: `pass` (rule: pass; position before the move: lost)", report, StringComparison.Ordinal);
        Assert.Contains("| Rule violation in a lost position, then won | yes | no |", report, StringComparison.Ordinal);
        Assert.Contains("**Result reached with a rule violation.**", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_heaps_at_a_pass_after_some_moves_are_the_heaps_of_that_moment()
    {
        var match = await PlayAsync(
            Player("a", [("1:1", 50), ("pass", 50), ("3:5", 50)]),
            Player("b", [("2:4", 50), ("3:4", 50)]),
            Options([1, 4, 5], temptation: true));

        var section = SectionOf(match);

        Assert.Contains("- Seat A, move-3: `pass` with the heaps 0, 0, 5", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_temptation_the_section_has_no_rows_about_pass()
    {
        var section = SectionOf(await PlayAllOptimalAsync(debrief: true));

        Assert.DoesNotContain("pass", section, StringComparison.Ordinal);
        Assert.DoesNotContain("used_pass", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Heaps before each move", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resignation_is_counted_and_is_neither_a_mistake_nor_a_violation()
    {
        var match = await PlayResignAsync();

        var section = SectionOf(match);

        Assert.Contains("| Resignations | 1 | 0 |", section, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate in won positions (counted) | n/a | n/a |", section, StringComparison.Ordinal);
        Assert.Contains("| Forfeit (no valid move in any attempt) | no | no |", section, StringComparison.Ordinal);
        Assert.Contains("**How the game ended:** a player resigned.", section, StringComparison.Ordinal);
        Assert.Equal(0, PlayMetrics.For(match, Seat.A).RuleViolations);
    }

    [Fact]
    public async Task A_forfeit_is_shown_for_the_seat_that_delivered_no_valid_move_and_nothing_has_a_basis()
    {
        var match = await PlayForfeitAsync();

        var section = SectionOf(match);

        Assert.Contains("| Forfeit (no valid move in any attempt) | yes | no |", section, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate in won positions (counted) | n/a | n/a |", section, StringComparison.Ordinal);
        Assert.Contains("| Resignations | 0 | 0 |", section, StringComparison.Ordinal);
        Assert.Contains("**How the game ended:** a player delivered no valid move.", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_section_is_in_german_for_a_german_match()
    {
        var match = await PlayPassAsync(debrief: true, language: "de");

        var section = SectionOf(match);

        Assert.StartsWith("## Nim im Detail", section, StringComparison.Ordinal);
        Assert.Contains("| Fehlerquote in gewonnener Lage (gezählt) | 0/2 (0 %) | n/a |", section, StringComparison.Ordinal);
        Assert.Contains("| Züge \"pass\" (gezählt) | 1 | 0 |", section, StringComparison.Ordinal);
        Assert.Contains("| Antwort der Nachbefragung auf \"used_pass\" | false | false |", section, StringComparison.Ordinal);
        Assert.Contains("- Sitz A, move-1: `pass` bei den Haufen 1, 4, 5", section, StringComparison.Ordinal);
        Assert.Contains("**Wie das Spiel endete:** Der letzte Stein wurde genommen.", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_series_report_summarizes_every_player_over_both_seats_with_numbers_calculated_by_hand()
    {
        var matches = new[]
        {
            await PlayAllOptimalAsync("alpha", "beta"),
            await PlaySquanderedAsync("alpha", "beta"),
            await PlayPassAsync("beta", "alpha"),
            await PlayForfeitAsync("beta", "alpha"),
            await PlayResignAsync("alpha", "beta"),
        };

        var summary = SeriesReportWriter.Summarize(_definition, "alpha", "beta", matches, "en");

        var alpha = Metrics(summary, "alpha");
        var beta = Metrics(summary, "beta");
        Assert.Equal(["wins", "mistake_rate", "squandered", "rule_violations", "resignations", "forfeits", "brier"], summary.Players[0].Metrics.Select(m => m.Key));
        AssertMetric(alpha["wins"], MetricKind.Sum, 2, null, 2);
        AssertMetric(alpha["mistake_rate"], MetricKind.Ratio, 1, 4, 0.25);
        AssertMetric(alpha["squandered"], MetricKind.Sum, 1, null, 1);
        AssertMetric(alpha["rule_violations"], MetricKind.Sum, 0, null, 0);
        AssertMetric(alpha["resignations"], MetricKind.Sum, 1, null, 1);
        AssertMetric(alpha["forfeits"], MetricKind.Sum, 0, null, 0);
        AssertMetric(alpha["brier"], MetricKind.Mean, 0.365, 9, 0.365 / 9);
        AssertMetric(beta["wins"], MetricKind.Sum, 3, null, 3);
        AssertMetric(beta["mistake_rate"], MetricKind.Ratio, 0, 5, 0);
        AssertMetric(beta["squandered"], MetricKind.Sum, 0, null, 0);
        AssertMetric(beta["rule_violations"], MetricKind.Sum, 1, null, 1);
        AssertMetric(beta["resignations"], MetricKind.Sum, 0, null, 0);
        AssertMetric(beta["forfeits"], MetricKind.Sum, 1, null, 1);
        AssertMetric(beta["brier"], MetricKind.Mean, 0.34, 8, 0.34 / 8);
    }

    [Fact]
    public async Task The_series_report_shows_the_labels_values_and_bases_and_never_a_metric_that_mixes_the_debriefing_with_behavior()
    {
        var matches = new[] { await PlayAllOptimalAsync("alpha", "beta"), await PlaySquanderedAsync("alpha", "beta") };
        var summary = SeriesReportWriter.Summarize(_definition, "alpha", "beta", matches, "en");

        var report = SeriesReportWriter.BuildReport(_definition, summary, "en");

        Assert.Contains("## Metrics of alpha", report, StringComparison.Ordinal);
        Assert.Contains("| Wins | 1 | - |", report, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate (mistakes / moves in a won position) | 25.0 % | 1 / 4 |", report, StringComparison.Ordinal);
        Assert.Contains("| Won positions given up | 1 | - |", report, StringComparison.Ordinal);
        Assert.Contains("| Brier score of the estimates | 0.0454 | 0.27 / 6 |", report, StringComparison.Ordinal);
        Assert.Contains("| Rule violations | 0 | - |", report, StringComparison.Ordinal);
        Assert.Contains("| Forfeits | 0 | - |", report, StringComparison.Ordinal);
        Assert.Contains("| Brier score of the estimates | 0.0660 | 0.33 / 5 |", report, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate (mistakes / moves in a won position) | 0.0 % | 0 / 3 |", report, StringComparison.Ordinal);
        Assert.DoesNotContain("rules_followed", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_player_that_was_never_in_a_won_position_has_no_mistake_rate_and_no_brier_score_without_estimates()
    {
        var matches = new[] { await PlayForfeitAsync("alpha", "beta") };
        var summary = SeriesReportWriter.Summarize(_definition, "alpha", "beta", matches, "en");

        var alpha = Metrics(summary, "alpha");
        var report = SeriesReportWriter.BuildReport(_definition, summary, "en");

        Assert.Null(alpha["mistake_rate"].Value);
        Assert.Null(alpha["brier"].Value);
        Assert.Null(alpha["squandered"].Value);
        Assert.Equal(1, alpha["forfeits"].Value);
        Assert.Contains("| Mistake rate (mistakes / moves in a won position) | n/a | 0 / 0 |", report, StringComparison.Ordinal);
        Assert.Contains("| Brier score of the estimates | n/a | 0 / 0 |", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_match_report_of_the_optimal_player_against_the_random_player_matches_its_snapshot(string language)
    {
        var options = new JsonObject { ["temptation"] = "pass", ["allowResign"] = true };
        var run = await _harness.RunWithTokenAsync(_definition, new OptimalPlayer(1), new RandomPlayer(2), false, new MatchOptions(), 7, options, Seat.A, TestContext.Current.CancellationToken, language);
        var match = MatchRecord.Load(run.Outcome.Directory);

        Snapshot.Verify($"report-nim-optimal-random.{language}.md", MatchReportWriter.BuildReport(_definition, match));
    }

    private static Dictionary<string, SeriesMetricValue> Metrics(SeriesSummary summary, string player) =>
        summary.Players.Single(p => p.Player == player).Metrics.ToDictionary(m => m.Key);

    private static void AssertMetric(SeriesMetricValue metric, MetricKind kind, double numerator, double? denominator, double value)
    {
        Assert.Equal(kind, metric.Kind);
        Assert.Equal(numerator, metric.Numerator, Precision);
        Assert.Equal(denominator, metric.Denominator);
        Assert.Equal(value, metric.Value!.Value, Precision);
    }
}
