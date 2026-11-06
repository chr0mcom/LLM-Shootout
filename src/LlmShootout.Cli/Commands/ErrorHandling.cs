using System.Text.Json;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Series;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>Turns the exceptions that mean "you used it wrongly" into a short message and an exit code, without a stack trace.</summary>
public static class ErrorHandling
{
    /// <summary>Exit code for usage errors.</summary>
    public const int UsageError = 2;

    /// <summary>Exit code for a run that was aborted (cost limit, timeout, failure).</summary>
    public const int Aborted = 3;

    /// <summary>Runs a command body and maps known exceptions to exit codes.</summary>
    public static async Task<int> RunAsync(IAnsiConsole console, Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (ex is UsageException or PlayerSpecException or SeriesFileException or GameOptionsException or FileNotFoundException or DirectoryNotFoundException or InvalidDataException or JsonException)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return UsageError;
        }
        catch (HttpRequestException ex)
        {
            console.MarkupLine($"[red]Network error:[/] {Markup.Escape(ex.Message)}");
            return UsageError;
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Cancelled.[/]");
            return 130;
        }
    }
}
