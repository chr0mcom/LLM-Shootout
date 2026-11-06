using System.CommandLine;
using LlmShootout.Arena.Protocol;
using LlmShootout.Cli.Output;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout replay: plays a stored match back, turn by turn.</summary>
public static class ReplayCommand
{
    /// <summary>Creates the command.</summary>
    public static Command Build(CliEnvironment env)
    {
        var directory = new Argument<string>("match") { Description = "A match directory." };
        var delay = new Option<int>("--delay") { Description = "Milliseconds to wait between turns (default 0).", DefaultValueFactory = _ => 0 };
        var step = new Option<bool>("--step") { Description = "Wait for a key before each turn. Only with a terminal." };
        var thoughts = new Option<bool>("--show-thoughts") { Description = "Also print the private thoughts." };
        var command = new Command("replay", "Replays a stored match in the console. Prints everything at once unless --delay or --step is given.") { directory, delay, step, thoughts };

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Console, () =>
        {
            var record = MatchRecord.Load(parse.GetRequiredValue(directory));
            var view = new LiveView(env.Console, parse.GetValue(thoughts));
            var interactive = parse.GetValue(step) && !Console.IsInputRedirected && !Console.IsOutputRedirected;
            if (parse.GetValue(step) && !interactive)
            {
                env.Console.MarkupLine("[dim](--step ignored: no terminal)[/]");
            }

            foreach (var e in record.Events)
            {
                ct.ThrowIfCancellationRequested();
                if (e is TurnEvent or MessageEvent && parse.GetValue(delay) > 0)
                {
                    Thread.Sleep(parse.GetValue(delay));
                }

                if (e is TurnEvent && interactive)
                {
                    Console.ReadKey(intercept: true);
                }

                view.Handle(e);
            }

            return Task.FromResult(0);
        }));
        return command;
    }
}
