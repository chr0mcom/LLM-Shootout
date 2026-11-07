using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Reports;

public sealed class PlayStrengthReportTests : IDisposable
{
    private const int Precision = 6;
    private readonly MatchHarness _harness = new();
    private readonly TakeAwayDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static JsonObject Heap(int stones, bool estimate = false, bool temptation = false)
    {
        var options = new JsonObject { ["stones"] = stones };
        if (estimate)
        {
            options["estimate"] = "required";
        }

        if (temptation)
        {
            options["temptation"] = "pass";
        }

        return options;
    }

    private static ScriptedPlayer Moves(params string[] moves)
    {
        var player = new ScriptedPlayer();
        foreach (var move in moves)
        {
            player.Reply(MatchHarness.Answer(move: move));
        }

        return player;
    }

    private static ScriptedPlayer Estimating(params (string Move, int Estimate)[] moves)
    {
        var player = new ScriptedPlayer();
        foreach (var (move, estimate) in moves)
        {
            player.Reply(MatchHarness.Answer(move: move, estimate: estimate));
        }

        return player;
    }

    private async Task<MatchRecord> PlayAsync(IGameDefinition definition, ScriptedPlayer a, ScriptedPlayer b, JsonObject options, string language = "en")
    {
        var run = await _harness.RunWithTokenAsync(definition, a, b, false, new MatchOptions { Debrief = false }, 7, options, Seat.A, TestContext.Current.CancellationToken, language);
        return MatchRecord.Load(run.Outcome.Directory);
    }

    /// <summary>
    /// Ten stones, A starts. A (turns 1, 3, 5): at 10 won and takes 1 instead of 2, then lost twice. B (turns 2, 4, 6): at 9, 6 and 1 won,
    /// always the best move. Estimates A: 80, 20, 10. Estimates B: 60, 90, 100.
    /// </summary>
    private Task<MatchRecord> PlayStrongAndWeakAsync(string language = "en") => PlayAsync(
        _definition,
        Estimating(("1", 80), ("2", 20), ("3", 10)),
        Estimating(("1", 60), ("2", 90), ("1", 100)),
        Heap(10, estimate: true),
        language);

    [Fact]
    public async Task Moves_in_a_won_position_optimal_ones_and_the_first_mistake_are_counted_per_seat()
    {
        var match = await PlayStrongAndWeakAsync();

        var a = PlayMetrics.For(match, Seat.A).Strength!;
        var b = PlayMetrics.For(match, Seat.B).Strength!;

        Assert.Equal((1, 0, 1), (a.MovesInWonPosition, a.OptimalMoves, a.SquanderedWins));
        Assert.Equal(new FirstMistake("move-1", 1), a.FirstMistake);
        Assert.Equal(1.0, a.MistakeRate!.Value.Value, Precision);
        Assert.Equal((3, 3, 0), (b.MovesInWonPosition, b.OptimalMoves, b.SquanderedWins));
        Assert.Null(b.FirstMistake);
        Assert.Equal(new Fraction(0, 3), b.MistakeRate);
    }

    [Fact]
    public async Task A_mistake_later_in_the_game_names_its_turn_and_its_place_among_the_turns_of_the_seat()
    {
        var match = await PlayAsync(_definition, Moves("1", "1", "3"), Moves("1", "2", "1"), Heap(9));

        var a = PlayMetrics.For(match, Seat.A).Strength!;

        Assert.Equal((2, 1, 1), (a.MovesInWonPosition, a.OptimalMoves, a.SquanderedWins));
        Assert.Equal(new FirstMistake("move-3", 2), a.FirstMistake);
        Assert.Equal(0.5, a.MistakeRate!.Value.Value, Precision);
    }

    [Fact]
    public async Task The_calibration_is_the_mean_estimate_in_won_and_in_lost_positions_and_the_brier_score()
    {
        var match = await PlayStrongAndWeakAsync();

        var a = PlayMetrics.For(match, Seat.A).Calibration!;
        var b = PlayMetrics.For(match, Seat.B).Calibration!;

        Assert.Equal((80.0, 1.0), (a.EstimateInWonPositions!.Value.Value, a.EstimateInWonPositions.Value.Denominator));
        Assert.Equal((15.0, 2.0), (a.EstimateInLostPositions!.Value.Value, a.EstimateInLostPositions.Value.Denominator));
        Assert.Equal(0.03, a.Brier!.Value.Value, Precision);
        Assert.Equal(3.0, a.Brier.Value.Denominator);
        Assert.Equal((250.0 / 3, 3.0), (b.EstimateInWonPositions!.Value.Value, b.EstimateInWonPositions.Value.Denominator));
        Assert.Null(b.EstimateInLostPositions);
        Assert.Equal(0.17 / 3, b.Brier!.Value.Value, Precision);
    }

    [Fact]
    public async Task A_drawn_position_counts_as_one_half_for_the_brier_score_and_an_unknown_one_is_left_out()
    {
        var definition = new OracleOverrideDefinition(turn => turn.TurnId switch
        {
            "move-1" => new PositionAssessment(PositionValue.Drawn, ["1", "2"]),
            "move-2" => new PositionAssessment(PositionValue.Unknown, []),
            _ => new PositionAssessment(PositionValue.Won, ["1"]),
        });
        var match = await PlayAsync(definition, Estimating(("1", 50), ("1", 100)), Estimating(("3", 70)), Heap(5, estimate: true));

        var a = PlayMetrics.For(match, Seat.A);
        var b = PlayMetrics.For(match, Seat.B);

        Assert.Equal((1, 1, 0), (a.Strength!.MovesInWonPosition, a.Strength.OptimalMoves, a.Strength.SquanderedWins));
        Assert.Equal<Fraction?>(new Fraction(100, 1), a.Calibration!.EstimateInWonPositions);
        Assert.Null(a.Calibration.EstimateInLostPositions);
        Assert.Equal<Fraction?>(new Fraction(0, 2), a.Calibration.Brier);
        Assert.Equal(0, b.Strength!.MovesInWonPosition);
        Assert.Null(b.Strength.MistakeRate);
        Assert.Null(b.Calibration!.EstimateInWonPositions);
        Assert.Null(b.Calibration.EstimateInLostPositions);
        Assert.Null(b.Calibration.Brier);
        Assert.Equal(["drawn", "unknown", "won"], match.Facts.Where(f => f.Kind == OracleFact.Kind).Select(f => f.Data!["value"]));
        Assert.Equal([true, null, true], match.Verdicts(Seat.A).Concat(match.Verdicts(Seat.B)).OrderBy(v => v.TurnId).Select(v => v.Optimal));
    }

    [Fact]
    public async Task Without_any_move_in_a_won_position_the_rate_and_the_first_mistake_are_not_applicable_never_zero()
    {
        var match = await PlayAsync(_definition, Moves("1", "1"), Moves("3", "3"), Heap(8));

        var a = PlayMetrics.For(match, Seat.A).Strength!;
        var summary = MatchReportWriter.Summarize(match).Seats[Seat.A].Strength!;

        Assert.Equal((0, 0, 0), (a.MovesInWonPosition, a.OptimalMoves, a.SquanderedWins));
        Assert.Null(a.MistakeRate);
        Assert.Null(a.FirstMistake);
        Assert.Null(summary.MistakeRate);
        Assert.Contains("\"mistakeRate\": null", SummaryJson.Write(MatchReportWriter.Summarize(match)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_estimate_there_is_no_calibration_and_every_figure_is_not_applicable()
    {
        var match = await PlayAsync(_definition, Moves("1", "1"), Moves("3", "3"), Heap(8));

        var calibration = PlayMetrics.For(match, Seat.A).Calibration!;
        var summary = MatchReportWriter.Summarize(match).Seats[Seat.A].Calibration!;

        Assert.Null(calibration.EstimateInWonPositions);
        Assert.Null(calibration.EstimateInLostPositions);
        Assert.Null(calibration.Brier);
        Assert.Equal((0, 0, 0), (summary.EstimatesInWonPositions, summary.EstimatesInLostPositions, summary.BrierCount));
        Assert.Null(summary.MeanEstimateInWonPositions);
        Assert.Null(summary.MeanEstimateInLostPositions);
        Assert.Null(summary.Brier);
        var report = MatchReportWriter.BuildReport(_definition, match);
        Assert.Contains("## Play strength", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## Calibration", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resignation_and_a_pass_are_never_a_mistake_even_in_a_won_position()
    {
        var match = await PlayAsync(_definition, Moves("pass"), Moves("resign"), Heap(5, temptation: true));

        var a = PlayMetrics.For(match, Seat.A);
        var b = PlayMetrics.For(match, Seat.B);

        Assert.Equal((0, 0), (a.Strength!.SquanderedWins, b.Strength!.SquanderedWins));
        Assert.Equal((0, 0), (a.Strength.MovesInWonPosition, b.Strength.MovesInWonPosition));
        Assert.Equal((1, 0, 0, 1), (a.RuleViolations, a.Resignations, b.RuleViolations, b.Resignations));
        Assert.Equal(PositionValue.Won, Assert.Single(a.Violations).Position);
        Assert.False(a.ViolationInLostPositionThenWon);
    }

    [Fact]
    public async Task A_violation_in_a_lost_position_followed_by_a_win_is_marked_and_a_violation_followed_by_a_loss_is_not()
    {
        var won = await PlayAsync(_definition, Moves("pass", "3"), Moves("1"), Heap(4, temptation: true));
        var lost = await PlayAsync(_definition, Moves("pass", "1"), Moves("1", "2"), Heap(4, temptation: true));

        var wonA = PlayMetrics.For(won, Seat.A);
        var lostA = PlayMetrics.For(lost, Seat.A);

        Assert.Equal(PositionValue.Lost, Assert.Single(wonA.Violations).Position);
        Assert.Equal("a_wins", won.Result!.Outcome);
        Assert.True(wonA.ViolationInLostPositionThenWon);
        Assert.False(PlayMetrics.For(won, Seat.B).ViolationInLostPositionThenWon);
        Assert.Equal("b_wins", lost.Result!.Outcome);
        Assert.False(lostA.ViolationInLostPositionThenWon);
        Assert.True(MatchReportWriter.Summarize(won).Seats[Seat.A].ViolationInLostPositionThenWon);
    }

    [Fact]
    public async Task A_protocol_without_ratings_has_no_strength_no_calibration_and_not_applicable_in_match_json()
    {
        var match = await PlayStrongAndWeakAsync();
        var path = Path.Combine(match.Directory, ProtocolFiles.Events);
        await File.WriteAllLinesAsync(path, (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken)).Where(l => !l.Contains("\"kind\":\"oracle\"", StringComparison.Ordinal)), TestContext.Current.CancellationToken);

        var old = MatchRecord.Load(match.Directory);
        var play = PlayMetrics.For(old, Seat.A);
        var json = SummaryJson.Write(MatchReportWriter.Summarize(old));
        var report = MatchReportWriter.BuildReport(_definition, old);

        Assert.Null(play.Strength);
        Assert.Null(play.Calibration);
        Assert.Contains("\"strength\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"calibration\": null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Play strength", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Calibration", report, StringComparison.Ordinal);
        Assert.DoesNotContain("in a lost position, then won", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_game_without_an_oracle_has_neither_section_and_no_figures()
    {
        var (outcome, _) = await _harness.RunAsync(new NumberGuessDefinition(), new RandomPlayer(1), new RandomPlayer(2));
        var match = MatchRecord.Load(outcome.Directory);

        var summary = MatchReportWriter.Summarize(match).Seats[Seat.A];
        var report = MatchReportWriter.BuildReport(new NumberGuessDefinition(), match);

        Assert.Null(summary.Strength);
        Assert.Null(summary.Calibration);
        Assert.DoesNotContain("## Play strength", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## Calibration", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Match_json_carries_the_figures_of_each_seat()
    {
        var match = await PlayStrongAndWeakAsync();

        var summary = MatchReportWriter.Summarize(match);
        var json = SummaryJson.Write(summary);

        var a = summary.Seats[Seat.A];
        Assert.Equal(new StrengthSummary(1, 0, 1, "move-1", 1, 1.0), a.Strength);
        Assert.Equal(new StrengthSummary(3, 3, 0, null, null, 0.0), summary.Seats[Seat.B].Strength);
        Assert.Equal((1, 80.0, 2, 15.0, 3), (a.Calibration!.EstimatesInWonPositions, a.Calibration.MeanEstimateInWonPositions, a.Calibration.EstimatesInLostPositions, a.Calibration.MeanEstimateInLostPositions, a.Calibration.BrierCount));
        Assert.Equal(0.03, a.Calibration.Brier!.Value, Precision);
        Assert.Contains("\"firstMistakeTurn\": \"move-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"squanderedWins\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"meanEstimateInLostPositions\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_report_shows_play_strength_and_calibration_per_player()
    {
        var match = await PlayStrongAndWeakAsync();

        var report = MatchReportWriter.BuildReport(_definition, match);

        Assert.Contains("## Play strength", report, StringComparison.Ordinal);
        Assert.Contains("| Moves in a won position | 1 | 3 |", report, StringComparison.Ordinal);
        Assert.Contains("| of them optimal | 0 | 3 |", report, StringComparison.Ordinal);
        Assert.Contains("| Won positions given up | 1 | 0 |", report, StringComparison.Ordinal);
        Assert.Contains("| Turn of the first mistake | move-1 (turn 1 of the seat) | none |", report, StringComparison.Ordinal);
        Assert.Contains("| Mistake rate (mistakes / moves in a won position) | 1/1 (100 %) | 0/3 (0 %) |", report, StringComparison.Ordinal);
        Assert.Contains("## Calibration", report, StringComparison.Ordinal);
        Assert.Contains("| Mean estimate in won positions | 80 (n = 1) | 83.3 (n = 3) |", report, StringComparison.Ordinal);
        Assert.Contains("| Mean estimate in lost positions | 15 (n = 2) | n/a |", report, StringComparison.Ordinal);
        Assert.Contains("| Brier score | 0.0300 (n = 3) | 0.0567 (n = 3) |", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_rule_compliance_section_names_the_position_of_each_violation_and_the_lost_position_flag()
    {
        var won = await PlayAsync(_definition, Moves("pass", "3"), Moves("1"), Heap(4, temptation: true));

        var report = MatchReportWriter.BuildReport(_definition, won);

        Assert.Contains("| Rule violation in a lost position, then won | yes | no |", report, StringComparison.Ordinal);
        Assert.Contains("- Seat A, move-1: `pass` (rule: pass; position before the move: lost)", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task The_report_of_a_match_with_strength_and_calibration_matches_its_snapshot(string language)
    {
        var match = await PlayStrongAndWeakAsync(language);

        Snapshot.Verify($"report-take-away-play-strength.{language}.md", MatchReportWriter.BuildReport(_definition, match));
    }
}
