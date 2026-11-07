using System.Text.Json;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Games.SplitOrSteal;
using LlmShootout.Tests.TestGames;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests.Series;

public sealed class SeriesAndReportTests : IDisposable
{
    private readonly MatchHarness _harness = new();
    private readonly SplitOrStealDefinition _definition = new();

    public void Dispose() => _harness.Dispose();

    private SeriesFile StandardSeries() => WriteSeries(
        "standard.json",
        """
        { "game": "split-or-steal", "seed": 2027, "matches": [
            { "communication": true, "firstSeat": "A" },
            { "communication": true, "firstSeat": "B" },
            { "communication": false, "firstSeat": "A" } ] }
        """);

    private SeriesFile WriteSeries(string name, string json)
    {
        Directory.CreateDirectory(_harness.Directory);
        var path = Path.Combine(_harness.Directory, name);
        File.WriteAllText(path, json);
        return SeriesFile.Load(path);
    }

    private async Task<(SeriesSummary Summary, string Directory)> RunSeriesAsync(string language = "de")
    {
        var directory = Path.Combine(_harness.Directory, Guid.NewGuid().ToString("N"));
        var file = StandardSeries();
        var plan = new SeriesPlan(_definition, file, new RandomPlayer(1), new RandomPlayer(2), language, new MatchOptions(), directory);
        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);
        return (summary, directory);
    }

    [Fact]
    public async Task The_standard_series_plays_three_matches_with_their_files_and_a_series_report()
    {
        var (summary, directory) = await RunSeriesAsync();

        Assert.Equal((3, 3, 0), (summary.Matches, summary.Completed, summary.Aborted));
        Assert.Equal(["01-A-first", "02-B-first", "03-A-first"], summary.Entries.Select(e => e.Directory));
        Assert.Equal([Seat.A, Seat.B, Seat.A], summary.Entries.Select(e => e.FirstSeat));
        foreach (var entry in summary.Entries)
        {
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "events.jsonl")));
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "match.json")));
            Assert.True(File.Exists(Path.Combine(directory, entry.Directory, "report.md")));
        }

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "series.json"), TestContext.Current.CancellationToken));
        Assert.Equal(3, json.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Equal("A", json.RootElement.GetProperty("entries")[0].GetProperty("firstSeat").GetString());
        Assert.False(json.RootElement.TryGetProperty("pointsTotal", out _));
        Assert.Equal(0, json.RootElement.GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task A_game_without_declared_metrics_gets_overview_and_list_but_no_table_per_player()
    {
        var (_, directory) = await RunSeriesAsync();

        var report = await File.ReadAllTextAsync(Path.Combine(directory, "series-report.md"), TestContext.Current.CancellationToken);

        Assert.Contains("## Überblick", report, StringComparison.Ordinal);
        Assert.Contains("| Nr | Verzeichnis | Startsitz | Ausgang | Kosten (USD) |", report, StringComparison.Ordinal);
        Assert.Contains("[01-A-first](01-A-first/report.md)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Kennzahlen", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Punkte", report, StringComparison.Ordinal);
        Assert.DoesNotContain("ungültig", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kommunikation", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_series_report_summarizes_the_declared_metrics_per_player_over_both_seats()
    {
        var file = WriteSeries("guess.json", "{ \"game\": \"number-guess\", \"seed\": 31, \"matches\": [ { \"firstSeat\": \"A\" }, { \"firstSeat\": \"B\" } ] }");
        var directory = Path.Combine(_harness.Directory, "guess");
        var plan = new SeriesPlan(new NumberGuessDefinition(), file, new NamedRandomPlayer("alpha", 1), new NamedRandomPlayer("beta", 2), "en", new MatchOptions { Debrief = false }, directory);

        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        var records = summary.Entries.Select(e => MatchRecord.Load(Path.Combine(directory, e.Directory))).ToArray();
        Assert.Equal(["alpha", "beta"], summary.Players.Select(p => p.Player));
        foreach (var player in summary.Players)
        {
            var seats = records.SelectMany(r => Seat.All.Where(s => r.Started.Players[s].Name == player.Player).Select(s => (Record: r, Seat: s))).ToArray();
            var guesses = seats.Sum(x => x.Record.Turns(x.Seat).Count());
            var hits = seats.Sum(x => x.Record.Result!.Seats[x.Seat].Score);
            var guessSum = seats.Sum(x => x.Record.Turns(x.Seat).Sum(t => double.Parse(t.Move!, System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(["guesses", "hit-rate", "never", "mean-guess"], player.Metrics.Select(m => m.Key));
            Assert.Equal((MetricKind.Mean, guessSum / guesses, (guessSum, (double)guesses)), (player.Metrics[3].Kind, player.Metrics[3].Value!.Value, (player.Metrics[3].Numerator, player.Metrics[3].Denominator!.Value)));
            Assert.Equal((double)guesses, player.Metrics[0].Value);
            Assert.Equal(hits / guesses, player.Metrics[1].Value);
            Assert.Equal((hits, (double)guesses), (player.Metrics[1].Numerator, player.Metrics[1].Denominator!.Value));
            Assert.Null(player.Metrics[2].Value);
        }
    }

    [Fact]
    public async Task A_metric_without_a_denominator_is_not_applicable_in_report_and_json()
    {
        var file = WriteSeries("guess-na.json", "{ \"game\": \"number-guess\", \"seed\": 31, \"matches\": [ { \"firstSeat\": \"A\" } ] }");
        var directory = Path.Combine(_harness.Directory, "guess-na");
        var plan = new SeriesPlan(new NumberGuessDefinition(), file, new NamedRandomPlayer("alpha", 1), new NamedRandomPlayer("beta", 2), "en", new MatchOptions { Debrief = false }, directory);

        await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        var report = await File.ReadAllTextAsync(Path.Combine(directory, "series-report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("## Metrics of alpha", report, StringComparison.Ordinal);
        Assert.Contains("| Never measurable | n/a | 0 / 0 |", report, StringComparison.Ordinal);
        Assert.Matches(@"\| Mean guess \| \d+\.\d{4} \| [\d.]+ / \d+ \|", report);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "series.json"), TestContext.Current.CancellationToken));
        var never = json.RootElement.GetProperty("players")[0].GetProperty("metrics")[2];
        Assert.Equal("never", never.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, never.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Rebuilding_a_series_report_gives_the_same_report()
    {
        var file = WriteSeries("guess-rebuild.json", "{ \"game\": \"number-guess\", \"seed\": 31, \"matches\": [ { \"firstSeat\": \"A\" }, { \"firstSeat\": \"B\" } ] }");
        var directory = Path.Combine(_harness.Directory, "guess-rebuild");
        var definition = new NumberGuessDefinition();
        var plan = new SeriesPlan(definition, file, new NamedRandomPlayer("alpha", 1), new NamedRandomPlayer("beta", 2), "en", new MatchOptions { Debrief = false }, directory);
        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);
        var records = summary.Entries.Select(e => MatchRecord.Load(Path.Combine(directory, e.Directory))).ToArray();

        var rebuilt = SeriesReportWriter.Summarize(definition, "alpha", "beta", records, "en");

        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "series-report.md"), TestContext.Current.CancellationToken), SeriesReportWriter.BuildReport(definition, rebuilt, "en"));
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
    public async Task A_series_file_without_a_message_setting_uses_the_default_of_the_game()
    {
        var file = WriteSeries(
            "defaults.json",
            """{ "game": "split-or-steal", "seed": 5, "matches": [ { "firstSeat": "A" }, { "communication": false }, { "communication": true } ] }""");
        var directory = Path.Combine(_harness.Directory, "defaults");
        var plan = new SeriesPlan(_definition, file, new RandomPlayer(1), new RandomPlayer(2), "en", new MatchOptions(), directory);

        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.Equal([null, false, true], file.Matches.Select(m => m.Communication));
        Assert.Equal([true, false, true], summary.Entries.Select(e => MatchRecord.Load(Path.Combine(directory, e.Directory)).Communication));
    }

    [Fact]
    public async Task A_match_of_a_series_does_not_depend_on_the_matches_before_it()
    {
        var (summary, directory) = await RunSeriesAsync("en");
        var file = StandardSeries();
        var second = file.Matches[1];
        var seed = file.Seed + 2;
        var alone = new MatchPlan(
            _definition,
            new GameSetup(seed, _definition.ResolveCommunication(second.Communication), "en", second.FirstSeat ?? Seat.A, second.Options),
            new Dictionary<Seat, IPlayer> { [Seat.A] = new RandomPlayer(seed + SeriesRunner.RandomSeedOffsetA), [Seat.B] = new RandomPlayer(seed + SeriesRunner.RandomSeedOffsetB) },
            new MatchOptions(),
            Path.Combine(_harness.Directory, "alone"));
        await new MatchRunner(_harness.Cost, _harness.Time).RunAsync(alone, TestContext.Current.CancellationToken);

        var inSeries = await File.ReadAllTextAsync(Path.Combine(directory, summary.Entries[1].Directory, "events.jsonl"), TestContext.Current.CancellationToken);
        var standalone = await File.ReadAllTextAsync(Path.Combine(alone.Directory, "events.jsonl"), TestContext.Current.CancellationToken);
        Assert.Equal(standalone, inSeries);
    }

    [Fact]
    public async Task The_first_seat_comes_from_the_file_or_else_from_the_seed()
    {
        var path = Path.Combine(_harness.Directory, "no-first.json");
        Directory.CreateDirectory(_harness.Directory);
        await File.WriteAllTextAsync(path, "{ \"game\": \"split-or-steal\", \"seed\": 5, \"matches\": [ {}, {}, {} ] }", TestContext.Current.CancellationToken);
        var plan = new SeriesPlan(_definition, SeriesFile.Load(path), new RandomPlayer(1), new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(_harness.Directory, "s"));

        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.All(summary.Entries, e => Assert.Matches("^0[123]-[AB]-first$", e.Directory));
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

        var plan = new SeriesPlan(_definition, StandardSeries(), costly, new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(harness.Directory, "s"));
        var summary = await new SeriesRunner(new MatchRunner(harness.Cost, harness.Time), harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        Assert.True(summary.Matches < 3);
        Assert.Equal(1, summary.Aborted);
    }

    [Theory]
    [InlineData("{ \"matches\": [] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"matches\": [ {} ] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"matches\": [ { \"firstSet\": \"A\" } ] }", "is not valid")]
    [InlineData("{ \"game\": \"x\", \"seed\": 1, \"matches\": [ { \"revealIdentity\": true } ] }", "revealIdentity")]
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
    public async Task Options_of_a_series_file_are_passed_to_the_game_unchanged()
    {
        var file = WriteSeries("options.json", """{ "game": "split-or-steal", "seed": 3, "matches": [ { "options": { "pot": 200, "messagesPerPlayer": 1 } } ] }""");
        var directory = Path.Combine(_harness.Directory, "options");
        var plan = new SeriesPlan(_definition, file, new RandomPlayer(1), new RandomPlayer(2), "en", new MatchOptions(), directory);

        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);

        var started = MatchRecord.Load(Path.Combine(directory, summary.Entries[0].Directory)).Started;
        Assert.Equal("""{"pot":200,"messagesPerPlayer":1}""", started.GameOptions!.ToJsonString());
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

        var plan = new SeriesPlan(_definition, StandardSeries(), costly, new RandomPlayer(2), "en", new MatchOptions { Debrief = false }, Path.Combine(harness.Directory, "s"));
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
    public void A_rematch_with_changed_conditions_is_only_configuration()
    {
        var rematch = WriteSeries(
            "rematch.json",
            """{ "game": "split-or-steal", "seed": 4054, "matches": [ { "firstSeat": "A", "options": { "pot": 1000, "messagesPerPlayer": 5 } } ] }""");

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
}

/// <summary>A random player with its own name, so that a series has two different players.</summary>
internal sealed class NamedRandomPlayer(string name, long seed) : IPlayer
{
    private readonly RandomPlayer _inner = new(seed);

    public string Name => name;

    public LlmShootout.Arena.Identity.ModelIdentity? Model => null;

    public Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken) => _inner.RespondAsync(request, cancellationToken);
}
