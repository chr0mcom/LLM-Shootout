using System.Diagnostics;
using LlmShootout.Arena;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Games;

namespace LlmShootout.Tests.Series;

public sealed class NimSeriesFilesTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string FilePath(string name) => Path.Combine(Snapshot.RepositoryRoot, "shootouts", name);

    private async Task<(SeriesSummary Summary, string Directory)> RunAsync(string name, IPlayer a, IPlayer b)
    {
        var file = SeriesFile.Load(FilePath(name));
        var directory = Path.Combine(_harness.Directory, name);
        var plan = new SeriesPlan(GameCatalog.Find(file.Game)!, file, a, b, "en", new MatchOptions(), directory);
        var summary = await new SeriesRunner(new MatchRunner(_harness.Cost, _harness.Time), _harness.Cost).RunAsync(plan, TestContext.Current.CancellationToken);
        return (summary, directory);
    }

    [Theory]
    [InlineData("nim.json")]
    [InlineData("nim-vs-optimal.json")]
    [InlineData("nim-misere.json")]
    public void Every_series_file_parses_for_Nim_without_messages_and_with_a_fixed_first_seat(string name)
    {
        var file = SeriesFile.Load(FilePath(name));

        Assert.Equal("nim", file.Game);
        Assert.All(file.Matches, m => Assert.False(m.Communication));
        Assert.All(file.Matches, m => Assert.NotNull(m.FirstSeat));
        Assert.All(file.Matches, m => Assert.NotNull(m.Options?["heaps"]));
    }

    [Fact]
    public void The_standard_series_swaps_the_first_seat_on_the_heaps_3_4_5()
    {
        var file = SeriesFile.Load(FilePath("nim.json"));

        Assert.Equal([Seat.A, Seat.B], file.Matches.Select(m => m.FirstSeat));
        Assert.All(file.Matches, m => Assert.Equal("[3,4,5]", m.Options!["heaps"]!.ToJsonString()));
        Assert.All(file.Matches, m => Assert.Null(m.Options!["misere"]));
    }

    [Fact]
    public void The_misere_series_swaps_the_first_seat_and_plays_misere()
    {
        var file = SeriesFile.Load(FilePath("nim-misere.json"));

        Assert.Equal([Seat.A, Seat.B], file.Matches.Select(m => m.FirstSeat));
        Assert.All(file.Matches, m => Assert.True(m.Options!["misere"]!.GetValue<bool>()));
    }

    [Fact]
    public void The_series_against_the_optimal_player_names_it_for_seat_B_and_starts_in_a_lost_position()
    {
        var file = SeriesFile.Load(FilePath("nim-vs-optimal.json"));

        var match = Assert.Single(file.Matches);
        Assert.Equal("optimal", file.PlayerB);
        Assert.Null(file.PlayerA);
        Assert.Equal(Seat.A, match.FirstSeat);
        Assert.Equal("[1,4,5]", match.Options!["heaps"]!.ToJsonString());
        Assert.Equal("pass", match.Options["temptation"]!.GetValue<string>());
        Assert.True(match.Options["allowResign"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("nim.json", 2)]
    [InlineData("nim-misere.json", 2)]
    public async Task The_series_files_run_through_with_random_and_optimal_players(string name, int matches)
    {
        var (summary, directory) = await RunAsync(name, new RandomPlayer(1), new OptimalPlayer(2));

        Assert.Equal((matches, matches, 0), (summary.Matches, summary.Completed, summary.Aborted));
        Assert.Equal(["01-A-first", "02-B-first"], summary.Entries.Select(e => e.Directory));
        Assert.True(File.Exists(Path.Combine(directory, "series-report.md")));
        Assert.All(summary.Entries, e => Assert.False(MatchRecord.Load(Path.Combine(directory, e.Directory)).Communication));
    }

    [Fact]
    public async Task The_series_against_the_optimal_player_runs_through_and_the_optimal_player_wins_the_lost_start()
    {
        var (summary, directory) = await RunAsync("nim-vs-optimal.json", new RandomPlayer(1), new OptimalPlayer(2));

        var entry = Assert.Single(summary.Entries);
        Assert.Equal("01-A-first", entry.Directory);
        var record = MatchRecord.Load(Path.Combine(directory, entry.Directory));
        Assert.Equal("b_wins", record.Result!.Outcome);
    }

    [Fact]
    public async Task The_example_script_creates_every_expected_example_directory_and_file()
    {
        var bash = new[] { "/bin/bash", "/usr/bin/bash" }.FirstOrDefault(File.Exists);
        Assert.SkipWhen(bash is null || !File.Exists(CliDll), "The example script needs bash and the built command line.");
        var output = Path.Combine(_harness.Directory, "examples");

        await RunScriptAsync(bash!, output);

        var matches = Directory.GetDirectories(output).Where(d => !d.EndsWith("_series", StringComparison.Ordinal)).ToArray();
        var series = Assert.Single(Directory.GetDirectories(output), d => d.EndsWith("_series", StringComparison.Ordinal));
        Assert.Equal(4, matches.Length);
        Assert.All(matches, m => Assert.All(["events.jsonl", "match.json", "report.md"], f => Assert.True(File.Exists(Path.Combine(m, f)), Path.Combine(m, f))));
        Assert.All(["series.json", "series-report.md"], f => Assert.True(File.Exists(Path.Combine(series, f)), f));
        Assert.All(matches.Concat(Directory.GetDirectories(series)), m => Assert.Equal("nim", MatchRecord.Load(m).GameId));
        Assert.Contains(matches, m => File.ReadAllText(Path.Combine(m, "report.md")).Contains("Rule violations in order", StringComparison.Ordinal));
    }

    private static string CliDll { get; } = Path.Combine(
        Snapshot.RepositoryRoot,
        "src",
        "LlmShootout.Cli",
        "bin",
        "Debug",
        "net10.0",
        "shootout.dll");

    private static async Task RunScriptAsync(string bash, string output)
    {
        var start = new ProcessStartInfo(bash) { WorkingDirectory = Snapshot.RepositoryRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("scripts/make-examples.sh");
        start.ArgumentList.Add(output);
        start.Environment["SHOOTOUT_NO_BUILD"] = "1";
        start.Environment.Remove("OPENROUTER_API_KEY");
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await error);
    }
}
