using System.Diagnostics;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Tests.Cli;

/// <summary>Runs the built application as a child process, the way a reader would.</summary>
public sealed class CliProcessTests : IDisposable
{
    private readonly string _work = TempDirectory.Create("cli");

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, true);
        }
    }

    private static string ShootoutDll { get; } = Path.Combine(
        Snapshot.RepositoryRoot,
        "src",
        "LlmShootout.Cli",
        "bin",
        Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)))!,
        Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)),
        "shootout.dll");

    private static async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Snapshot.RepositoryRoot,
        };
        start.ArgumentList.Add(ShootoutDll);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment.Remove("OPENROUTER_API_KEY");
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output, await error);
    }

    [Fact]
    public async Task Games_lists_split_or_steal()
    {
        var (exit, output, _) = await RunAsync("games");

        Assert.Equal(0, exit);
        Assert.Contains("split-or-steal", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Games_lists_Nim_before_split_or_steal_with_messages_off()
    {
        var (exit, output, _) = await RunAsync("games");

        Assert.Equal(0, exit);
        Assert.True(output.IndexOf("nim", StringComparison.Ordinal) < output.IndexOf("split-or-steal", StringComparison.Ordinal));
        Assert.Matches(@"nim\s+│\s+Nim\s+│\s+off\b", output);
    }

    [Fact]
    public async Task Play_nim_takes_the_heaps_the_temptation_and_the_resignation_as_game_options()
    {
        var (exit, output, _) = await RunAsync(
            "play", "nim", "--a", "random", "--b", "optimal", "--seed", "5", "--lang", "en", "--quiet",
            "--game-option", "heaps=[1,4,5]", "--game-option", "temptation=pass", "--game-option", "allowResign=true", "--out", _work);

        Assert.Equal(0, exit);
        var directory = output.Trim();
        var started = File.ReadLines(Path.Combine(directory, "events.jsonl")).First();
        Assert.Contains("\"gameOptions\":{\"heaps\":[1,4,5],\"temptation\":\"pass\",\"allowResign\":true}", started, StringComparison.Ordinal);
        Assert.Contains("Starting position, heaps 1 to 3: 1, 4, 5", File.ReadAllText(Path.Combine(directory, "report.md")), StringComparison.Ordinal);
        Assert.False(MatchRecord.Load(directory).Communication);
    }

    [Fact]
    public async Task A_wrong_Nim_option_is_a_usage_error_before_anything_is_written()
    {
        var (exit, _, error) = await RunAsync("play", "nim", "--a", "random", "--b", "optimal", "--game-option", "heaps=[0,4]", "--out", _work);

        Assert.Equal(2, exit);
        Assert.NotEmpty(error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_work));
    }

    [Fact]
    public async Task The_series_against_the_optimal_player_takes_seat_B_from_the_file()
    {
        var (exit, output, _) = await RunAsync("series", "shootouts/nim-vs-optimal.json", "--a", "random", "--lang", "en", "--quiet", "--out", _work);

        Assert.Equal(0, exit);
        Assert.Contains("optimal", Path.GetFileName(output.Trim()), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output.Trim(), "01-A-first", "report.md")));
    }

    [Fact]
    public async Task Games_shows_the_default_of_the_messages_setting()
    {
        var (_, output, _) = await RunAsync("games");

        Assert.Matches(@"split-or-steal\s+│\s+Split or Steal\s+│\s+on\b", output);
        Assert.Contains("messages between players", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public async Task Play_resolves_the_messages_setting_from_the_option_or_the_default_of_the_game(string? option, bool expected)
    {
        string[] args = ["play", "split-or-steal", "--a", "random", "--b", "random", "--seed", "3", "--lang", "en", "--quiet", "--out", _work];

        var (exit, output, _) = await RunAsync(option is null ? args : [.. args, "--communication", option]);

        Assert.Equal(0, exit);
        Assert.Equal(expected, MatchRecord.Load(output.Trim()).Communication);
    }

    [Fact]
    public async Task Play_with_random_players_runs_without_a_key_and_writes_the_files()
    {
        var (exit, output, _) = await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--seed", "3", "--lang", "en", "--out", _work);

        Assert.Equal(0, exit);
        Assert.Contains("Result:", output, StringComparison.Ordinal);
        var match = Directory.GetDirectories(_work).Single();
        Assert.All(new[] { "events.jsonl", "match.json", "report.md" }, f => Assert.True(File.Exists(Path.Combine(match, f)), f));
    }

    [Fact]
    public async Task Quiet_mode_prints_only_the_directory()
    {
        var (exit, output, _) = await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--quiet", "--out", _work);

        Assert.Equal(0, exit);
        Assert.True(Directory.Exists(output.Trim()), output);
    }

    [Fact]
    public async Task The_models_command_reads_a_saved_response_and_filters()
    {
        var (exit, output, _) = await RunAsync("models", "--from", "docs/examples/models-sample.json", "--filter", "gpt-oss");

        Assert.Equal(0, exit);
        Assert.Contains("openai/gpt-oss-20b", output, StringComparison.Ordinal);
        Assert.DoesNotContain("mistral-nemo", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("play", "split-or-steal", "--a", "nowhere:some-model", "--b", "random")]
    [InlineData("models", "--endpoint", "nowhere")]
    public async Task An_unknown_endpoint_is_a_usage_error_that_lists_the_configured_ones(params string[] args)
    {
        var (exit, _, error) = await RunAsync(args);

        Assert.Equal(2, exit);
        Assert.Contains("Unknown endpoint 'nowhere'", error, StringComparison.Ordinal);
        Assert.Contains("openrouter", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY")]
    public async Task A_model_without_an_api_key_gets_a_clear_message_and_exit_code_2(string variable)
    {
        var (exit, _, error) = await RunAsync("play", "split-or-steal", "--a", "openai/gpt-4o", "--b", "random", "--out", _work);

        Assert.Equal(2, exit);
        Assert.Contains(variable, error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("play", "chess", "--a", "random", "--b", "random")]
    [InlineData("play", "split-or-steal", "--a", "random")]
    [InlineData("play", "split-or-steal", "--a", "random", "--b", "random", "--communication", "maybe")]
    [InlineData("series", "does-not-exist.json", "--a", "random", "--b", "random")]
    [InlineData("replay", "/no/such/dir")]
    [InlineData("report", "/no/such/dir")]
    [InlineData("nonsense")]
    public async Task Usage_errors_give_exit_code_2_and_a_short_message(params string[] args)
    {
        var (exit, _, error) = await RunAsync(args);

        Assert.Equal(2, exit);
        Assert.NotEmpty(error);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_optimal_player_is_refused_for_a_game_without_an_oracle_with_exit_code_2_and_no_directory()
    {
        var (exit, _, error) = await RunAsync("play", "split-or-steal", "--a", "optimal", "--b", "random", "--out", _work);

        Assert.Equal(2, exit);
        Assert.Contains("Player 'optimal' needs a game that can name the best move; 'split-or-steal' cannot.", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_work));
    }

    [Fact]
    public async Task A_series_with_the_optimal_player_is_refused_before_anything_is_written()
    {
        var (exit, _, error) = await RunAsync("series", SeriesFilePath(), "--a", "random", "--b", "optimal", "--out", _work);

        Assert.Equal(2, exit);
        Assert.Contains("needs a game that can name the best move", error, StringComparison.Ordinal);
        Assert.Equal([Path.Combine(_work, "series.json")], Directory.EnumerateFileSystemEntries(_work));
    }

    [Fact]
    public async Task The_help_of_play_names_the_optimal_player()
    {
        var (exit, output, _) = await RunAsync("play", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("optimal", output, StringComparison.Ordinal);
    }

    private string SeriesFilePath()
    {
        var path = Path.Combine(_work, "series.json");
        File.WriteAllText(path, """{ "game": "split-or-steal", "seed": 2027, "language": "de", "matches": [ { "firstSeat": "A" }, { "firstSeat": "B", "communication": false } ] }""");
        return path;
    }

    [Fact]
    public async Task A_series_replay_report_debrief_round_trip_works()
    {
        var (exit, output, _) = await RunAsync("series", SeriesFilePath(), "--a", "random", "--b", "random", "--quiet", "--out", _work);
        Assert.Equal(0, exit);
        var series = output.Trim();
        var first = Path.Combine(series, "01-A-first");

        Assert.Equal(0, (await RunAsync("replay", first)).Exit);
        Assert.Equal(0, (await RunAsync("debrief", first)).Exit);
        Assert.Equal(0, (await RunAsync("report", series)).Exit);
        Assert.Contains("## Überblick", File.ReadAllText(Path.Combine(series, "series-report.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_match_stopped_by_the_cost_limit_exits_with_3_and_says_why_even_when_quiet()
    {
        var (exit, output, error) = await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--max-cost", "0", "--quiet", "--out", _work);

        Assert.Equal(3, exit);
        Assert.True(Directory.Exists(output.Trim()));
        Assert.Contains("cost_limit", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_series_that_ends_early_exits_with_3()
    {
        var (exit, _, error) = await RunAsync("series", SeriesFilePath(), "--a", "random", "--b", "random", "--max-cost", "0", "--quiet", "--out", _work);

        Assert.Equal(3, exit);
        Assert.Contains("ended early", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("B")]
    public async Task The_first_seat_is_accepted_in_either_case(string seat)
    {
        var (exit, output, _) = await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--first-seat", seat, "--quiet", "--out", _work);

        Assert.Equal(0, exit);
        Assert.True(Directory.Exists(output.Trim()));
    }

    [Fact]
    public async Task Report_on_a_plain_folder_of_matches_does_not_invent_a_series()
    {
        await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--quiet", "--out", _work);
        await RunAsync("play", "split-or-steal", "--a", "random", "--b", "random", "--quiet", "--out", _work, "--seed", "5");

        var (exit, _, _) = await RunAsync("report", _work);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(_work, "series.json")));
        Assert.All(Directory.GetDirectories(_work), d => Assert.True(File.Exists(Path.Combine(d, "report.md"))));
    }
}
