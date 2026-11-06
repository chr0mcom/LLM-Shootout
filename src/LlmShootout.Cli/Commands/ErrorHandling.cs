using System.Text.Json;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Series;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>
/// Turns exceptions into a short message and an exit code, without a stack trace:
/// 0 success, 1 unexpected error, 2 usage error, 3 run aborted (cost limit, timeout, failure), 130 cancelled.
/// </summary>
public static class ErrorHandling
{
    /// <summary>Exit code for usage errors.</summary>
    public const int UsageError = 2;

    /// <summary>Exit code for a run that was aborted or ended early (cost limit, timeout, failure).</summary>
    public const int Aborted = 3;

    /// <summary>Runs a command body and maps exceptions to exit codes.</summary>
    public static async Task<int> RunAsync(IAnsiConsole console, Func<Task<int>> body, CancellationToken cancellationToken)
    {
        try
        {
            return await body();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            console.MarkupLine("[yellow]Cancelled.[/]");
            return 130;
        }
        catch (BudgetExceededException ex)
        {
            console.MarkupLine($"[yellow]Stopped:[/] {Markup.Escape(ex.Message)}");
            return Aborted;
        }
        catch (Exception ex) when (ex is UsageException or OptionsValidationException or PlayerSpecException or SeriesFileException or GameOptionsException
            or FileNotFoundException or DirectoryNotFoundException or InvalidDataException or JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return UsageError;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            console.MarkupLine($"[red]Network error:[/] {Markup.Escape(ex.Message)}");
            return UsageError;
        }
        catch (Exception ex) when (!IsDebug)
        {
            console.MarkupLine($"[red]Unexpected error ({Markup.Escape(ex.GetType().Name)}):[/] {Markup.Escape(ex.Message)}");
            console.WriteLine("Set LLMSHOOTOUT_LOG=Debug to see the stack trace.");
            return 1;
        }
    }

    private static bool IsDebug =>
        Environment.GetEnvironmentVariable("LLMSHOOTOUT_LOG") is { } level && (level.Equals("Debug", StringComparison.OrdinalIgnoreCase) || level.Equals("Trace", StringComparison.OrdinalIgnoreCase));
}
