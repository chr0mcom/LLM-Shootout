using Spectre.Console;

namespace LlmShootout.Cli.Output;

/// <summary>Creates consoles that work in a terminal and when output is redirected to a file or a pipe.</summary>
public static class ConsoleFactory
{
    /// <summary>Creates a console on a writer. Colors only in a terminal; wide lines when redirected, so tables are not cut.</summary>
    public static IAnsiConsole Create(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        if (Console.IsOutputRedirected)
        {
            console.Profile.Width = 200;
        }

        return console;
    }
}
