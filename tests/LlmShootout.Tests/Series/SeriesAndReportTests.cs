using System.Text.Json;
using LlmShootout.Arena;
using LlmShootout.Arena.Annotation;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Games.SplitOrSteal;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests.Series;

public sealed class SeriesAndReportTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly SplitOrStealDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private static string StandardSeries => Path.Combine(Snapshot.RepositoryRoot, "shootouts", "split-or-steal.json");

    private async Task<(SeriesSummary Summary, string Directory)> RunSeriesAsync(string language = "de")
    {
        var directory = Path.Combine(_harness.Directory, Guid.NewGuid().ToString("N"));
        var file = SeriesFile.Load(StandardSeries);
        var plan = new SeriesPlan(_definition, file, new RandomPlayer(1), new RandomPlayer(2), language, new MatchOptions(), directory);
        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);
        return (summary, directory);
    }

    [Fact]
    public async Task The_standard_series_plays_three_matches_with_their_files_and_a_series_report()
    {
        var (summary, directory) = await RunSeriesAsync();

        Assert.Equal((3, 3, 0), (summary.Matches, summary.Completed, summary.Aborted));
        Assert.Equal(["01-talk-A-first", "02-talk-B-first", "03-silent-A-first"], summary.Entries.Select(e => e.Directory));
        foreach (var entry in summary.Entries)
        {
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "events.jsonl")));
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "match.json")));
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "report.md")));
        }

        var report = await File.ReadAllTextAsync(Path.Combine(directory, "series-report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("Vergleich mit und ohne Kommunikation", report, StringComparison.Ordinal);
        Assert.Contains("| mit | 2 |", report, StringComparison.Ordinal);
        Assert.Contains("| ohne | 1 |", report, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "series.json"), TestContext.Current.CancellationToken));
        Assert.Equal(3, json.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task The_series_is_reproducible_from_its_seed()
    {
        var first = await RunSeriesAsync();
        var second = await RunSeriesAsync();

        for (var i = 0; i < 3; i++)
        {
            var a = await File.ReadAllTextAsync(Path.Combine(first.Directory, first.Summary.Entries[i].Directory, "events.jsonl"), TestContext.Current.CancellationToken);
            var b = await File.ReadAllTextAsync(Path.Combine(second.Directory, second.Summary.Entries[i].Directory, "events.jsonl"), TestContext.Current.CancellationToken);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public async Task The_first_seat_comes_from_the_file_or_else_from_the_seed()
    {
        var path = Path.Combine(_harness.Directory, "no-first.json");
        Directory.CreateDirectory(_harness.Directory);
        await File.WriteAllTextAsync(path, "{ \"game\": \"split-or-steal\", \"seed\": 5, \"matches\": [ {}, {}, {} ] }", TestContext.Current.CancellationToken);
        var plan = new SeriesPlan(_definition, SeriesFile.Load(path), new RandomPlayer(1), new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(_harness.Directory, "s"));

        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.All(summary.Entries, e => Assert.Matches("^0[123]-talk-[AB]-first$", e.Directory));
        Assert.Equal(3, summary.Matches);
    }

    [Fact]
    public async Task A_series_stops_when_the_cost_limit_is_reached()
    {
        using var harness = new MatchHarness(maxCostUsd: 0.01m);
        var costly = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y"));
        for (var i = 0; i < 30; i++)
        {
            costly.Reply(MatchHarness.Answer("t", "hi", "split"), info: MatchHarness.Info(0.02m));
        }

        var plan = new SeriesPlan(_definition, SeriesFile.Load(StandardSeries), costly, new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(harness.Directory, "s"));
        var summary = await new SeriesRunner(new MatchRunner(harness.Cost, harness.Time), harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.True(summary.Matches < 3);
        Assert.Equal(1, summary.Aborted);
    }

    [Theory]
    [InlineData("{ \"matches\": [] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"matches\": [ {} ] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"matches\": [ { \"communicaton\": false } ] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"mathces\": [] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"matches\": [] }", "at least one entry")]
    [InlineData("not json", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"matches\": [ { \"firstSeat\": \"C\" } ] }", "is not valid")]
    public void Series_file_errors_are_explained(string content, string expected)
    {
        Directory.CreateDirectory(_harness.Directory);
        var path = Path.Combine(_harness.Directory, "bad.json");
        File.WriteAllText(path, content);

        var ex = Assert.Throws<SeriesFileException>(() => SeriesFile.Load(path));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Contains("bad.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_game_options_in_a_later_match_are_found_before_anything_is_played()
    {
        var path = Path.Combine(_harness.Directory, "late-error.json");
        Directory.CreateDirectory(_harness.Directory);
        await File.WriteAllTextAsync(path, "{ \"game\": \"split-or-steal\", \"seed\": 1, \"matches\": [ {}, { \"options\": { \"pot\": 7 } } ] }", TestContext.Current.CancellationToken);
        var scripted = new ScriptedPlayer("never");
        var plan = new SeriesPlan(_definition, SeriesFile.Load(path), scripted, scripted, "en", new MatchOptions(), Path.Combine(_harness.Directory, "late"));

        var ex = await Assert.ThrowsAsync<GameOptionsException>(() => new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken));

        Assert.Contains("Match 2", ex.Message, StringComparison.Ordinal);
        Assert.Empty(scripted.Calls);
        Assert.False(Directory.Exists(Path.Combine(_harness.Directory, "late")));
    }

    [Fact]
    public async Task No_further_match_starts_once_the_budget_is_used_up()
    {
        using var harness = new MatchHarness(maxCostUsd: 0.05m);
        var costly = new ScriptedPlayer("m", new LlmShootout.Arena.Identity.ModelIdentity("x/y"));
        for (var i = 0; i < 10; i++)
        {
            costly.Reply(MatchHarness.Answer("t", "hi", "split"), info: MatchHarness.Info(0.03m));
        }

        var plan = new SeriesPlan(_definition, SeriesFile.Load(StandardSeries), costly, new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(harness.Directory, "s"));
        var summary = await new SeriesRunner(new MatchRunner(harness.Cost, harness.Time), harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.True(harness.Cost.IsExhausted);
        Assert.Equal(summary.Entries.Count, summary.Matches);
        Assert.True(summary.Matches < 3);
    }

    [Fact]
    public void A_missing_series_file_is_reported()
    {
        Assert.Contains("does not exist", Assert.Throws<SeriesFileException>(() => SeriesFile.Load("/no/such/series.json")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_series_files_are_valid()
    {
        foreach (var name in new[] { "split-or-steal.json", "split-or-steal-rematch.json" })
        {
            var file = SeriesFile.Load(Path.Combine(Snapshot.RepositoryRoot, "shootouts", name));
            Assert.Equal("split-or-steal", file.Game);
            Assert.Equal(3, file.Matches.Count);
        }
    }

    [Fact]
    public void A_rematch_with_changed_conditions_is_only_configuration()
    {
        var rematch = SeriesFile.Load(Path.Combine(Snapshot.RepositoryRoot, "shootouts", "split-or-steal-rematch.json"));

        Assert.All(rematch.Matches, m => Assert.True(m.RevealIdentity));
        Assert.Equal(1000, rematch.Matches[0].Options!["pot"]!.GetValue<int>());
        var game = _definition.Create(new GameSetup(1, true, "de", Seat.A, rematch.Matches[0].Options));
        Assert.NotEmpty(game.PendingTurns);
    }

    [Fact]
    public void Directory_names_are_readable_and_unique()
    {
        var date = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(_harness.Directory);

        var first = MatchPaths.Match(_harness.Directory, date, "split-or-steal", "openrouter:openai/gpt-4o", "anthropic/claude-sonnet");
        Directory.CreateDirectory(first);
        var second = MatchPaths.Match(_harness.Directory, date, "split-or-steal", "openrouter:openai/gpt-4o", "anthropic/claude-sonnet");

        Assert.EndsWith("2026-11-20_split-or-steal_gpt-4o_vs_claude-sonnet_01", first, StringComparison.Ordinal);
        Assert.EndsWith("_02", second, StringComparison.Ordinal);
        Assert.EndsWith("_series", MatchPaths.Series(_harness.Directory, date, "split-or-steal", "a", "b"), StringComparison.Ordinal);
        Assert.Equal("player", MatchPaths.Slug(":::"));
    }

    [Fact]
    public async Task The_match_report_matches_its_snapshot_in_both_languages()
    {
        foreach (var language in new[] { "de", "en" })
        {
            var plan = new MatchPlan(
                _definition,
                new GameSetup(11, true, language, Seat.A),
                new Dictionary<Seat, IPlayer> { [Seat.A] = new RandomPlayer(1), [Seat.B] = new RandomPlayer(2) },
                new MatchOptions(),
                Path.Combine(_harness.Directory, language));
            await new MatchRunner(_harness.Cost, new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero))).RunAsync(plan, TestContext.Current.CancellationToken);
            var record = MatchRecord.Load(plan.Directory);
            await MatchReportWriter.WriteAsync(_definition, record, TestContext.Current.CancellationToken);

            Snapshot.Verify($"report-split-or-steal-random.{language}.md", await File.ReadAllTextAsync(Path.Combine(plan.Directory, "report.md"), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_judge_rating_exposes_a_broken_promise_and_a_contradicting_self_report()
    {
        var a = new ScriptedPlayer("a").Reply(MatchHarness.Answer("t", "I will split, promise")).Reply(MatchHarness.Answer("t", "split, really")).Reply(MatchHarness.Answer("t", "split")).Reply(MatchHarness.Answer("t", move: "steal"));
        var b = new ScriptedPlayer("b").Reply(MatchHarness.Answer("t", "ok")).Reply(MatchHarness.Answer("t", "ok")).Reply(MatchHarness.Answer("t", "ok")).Reply(MatchHarness.Answer("t", move: "split"));
        a.Reply(Debriefing.SplitOrStealMatchTests.DebriefJson(truthful: "true"));
        b.Reply(Debriefing.SplitOrStealMatchTests.DebriefJson(truthful: "true"));
        var (outcome, _) = await _harness.RunAsync(_definition, a, b, true, new MatchOptions(), debrief: true);
        var record = MatchRecord.Load(outcome.Directory);
        var before = await File.ReadAllBytesAsync(Path.Combine(outcome.Directory, "events.jsonl"), TestContext.Current.CancellationToken);

        var judge = new ScriptedPlayer("judge-model")
            .Reply("{\"announced\":\"split\",\"quote\":\"I will split, promise\",\"confidence\":0.9}")
            .Reply("{\"announced\":\"none\",\"quote\":null,\"confidence\":0.8}");
        var path = await new Annotator(new PlayerGateway(_harness.Cost), _harness.Time).RunAsync(_definition.AnnotationTask!, record, judge, TestContext.Current.CancellationToken);

        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(outcome.Directory, "events.jsonl"), TestContext.Current.CancellationToken));
        Assert.Contains("derived assessment", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        var annotated = MatchRecord.Load(outcome.Directory);
        var report = MatchReportWriter.BuildReport(_definition, annotated);
        File.WriteAllText("/tmp/claude-1000/-srv-docker/033e6862-94ac-4195-917d-1241fe1e3f38/scratchpad/judge-report.md", report);
        Assert.Contains("Broken word (announced split, decided steal)", report, StringComparison.Ordinal);
        Assert.Contains("Self-report contradicts behavior", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_judge_the_report_shows_messages_and_decision_side_by_side()
    {
        var (outcome, _) = await _harness.RunAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), true);
        var report = MatchReportWriter.BuildReport(_definition, MatchRecord.Load(outcome.Directory));

        Assert.Contains("There is no judge assessment", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_judge_answer_is_recorded_as_an_error_not_a_crash()
    {
        var (outcome, _) = await _harness.RunAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), true);
        var judge = new ScriptedPlayer("j").Reply("I cannot say.").Fail("HTTP 500");

        var path = await new Annotator(new PlayerGateway(_harness.Cost), _harness.Time).RunAsync(_definition.AnnotationTask!, MatchRecord.Load(outcome.Directory), judge, TestContext.Current.CancellationToken);

        var file = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), AnnotationJson.Default.AnnotationFile)!;
        Assert.All(file.Entries, e => Assert.NotNull(e.Error));
    }

    [Fact]
    public async Task Random_judges_work_without_an_api_key()
    {
        var (outcome, _) = await _harness.RunAsync(_definition, new RandomPlayer(1), new RandomPlayer(2), true);

        var path = await new Annotator(new PlayerGateway(_harness.Cost), _harness.Time).RunAsync(_definition.AnnotationTask!, MatchRecord.Load(outcome.Directory), new RandomPlayer(3), TestContext.Current.CancellationToken);

        var file = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), AnnotationJson.Default.AnnotationFile)!;
        Assert.All(file.Entries, e => Assert.Equal("unclear", e.Result!["announced"]!.ToString()));
    }
}
