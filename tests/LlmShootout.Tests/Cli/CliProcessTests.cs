using System.Diagnostics;

namespace LlmShootout.Tests.Cli;

/// <summary>Runs the built application as a child process, the way a reader would.</summary>
public sealed class CliProcessTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "llm-shootout-cli", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, true);
        }
    }

    private static async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Snapshot.RepositoryRoot,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "shootout.dll"));
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
    public async Task A_series_replay_report_debrief_annotate_round_trip_works()
    {
        var (exit, output, _) = await RunAsync("series", "shootouts/split-or-steal.json", "--a", "random", "--b", "random", "--quiet", "--out", _work);
        Assert.Equal(0, exit);
        var series = output.Trim();
        var first = Path.Combine(series, "01-talk-A-first");

        Assert.Equal(0, (await RunAsync("replay", first)).Exit);
        Assert.Equal(0, (await RunAsync("annotate", series, "--judge", "random")).Exit);
        Assert.Equal(0, (await RunAsync("debrief", first)).Exit);
        Assert.Equal(0, (await RunAsync("report", series)).Exit);
        Assert.Contains("Vergleich mit und ohne Kommunikation", File.ReadAllText(Path.Combine(series, "series-report.md")), StringComparison.Ordinal);
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
        var (exit, _, error) = await RunAsync("series", "shootouts/split-or-steal.json", "--a", "random", "--b", "random", "--max-cost", "0", "--quiet", "--out", _work);

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
