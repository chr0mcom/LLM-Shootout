using System.CommandLine;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Arena.Templates;
using LlmShootout.Cli.Output;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout series: a series file with several matches.</summary>
public static class SeriesCommand
{
    /// <summary>Creates the command.</summary>
    public static Command Build(CliEnvironment env)
    {
        var run = new RunOptionSet();
        var file = new Argument<string>("file") { Description = "Series file, for example shootouts/split-or-steal.json" };
        var command = new Command("series", "Plays all matches of a series file with the same two players.") { file };
        foreach (var option in run.All)
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var settings = run.Read(parse, env.BaseOptions);
            var series = SeriesFile.Load(parse.GetRequiredValue(file));
            var definition = CliEnvironment.RequireGame(series.Game);
            var specA = settings.PlayerA ?? series.PlayerA ?? throw new UsageException("The series file names no player for seat A. Pass --a <player> (use 'random' to play without an API key).");
            var specB = settings.PlayerB ?? series.PlayerB ?? throw new UsageException("The series file names no player for seat B. Pass --b <player>.");

            var cost = new CostMeter(settings.Match.MaxCostUsd);
            var players = (A: await env.CreatePlayerAsync(specA, series.Seed + 1000, settings.Endpoint, ct), B: await env.CreatePlayerAsync(specB, series.Seed + 2000, settings.Endpoint, ct));
            var directory = MatchPaths.Series(settings.OutDirectory, env.Time.GetUtcNow(), definition.Id, players.A.Name, players.B.Name);
            var view = new LiveView(env.Console, settings.ShowThoughts, cost);
            var runner = new SeriesRunner(env.CreateRunner(cost, settings.Quiet ? null : view.Handle), cost)
            {
                OnMatchFinished = (number, outcome) =>
                {
                    if (!settings.Quiet)
                    {
                        env.Console.MarkupLine($"[dim]Match {number} finished: {Markup.Escape(outcome.Result?.Outcome ?? outcome.AbortReason ?? "?")}[/]\n");
                    }
                },
            };

            var plan = new SeriesPlan(definition, series, players.A, players.B, settings.Language ?? series.Language ?? ArenaTemplates.DefaultLanguage, settings.Match, directory);
            var summary = await runner.RunAsync(plan, ct);

            env.Console.WriteLine(settings.Quiet ? directory : $"Series report: {Path.Combine(directory, SeriesReportWriter.ReportFile)}");
            var complete = summary.Aborted == 0 && summary.Matches == series.Matches.Count;
            if (!complete)
            {
                env.Error.MarkupLine($"[yellow]The series ended early:[/] {summary.Matches} of {series.Matches.Count} matches were played, {summary.Aborted} aborted (cost limit, timeout or failure).");
            }

            return complete ? 0 : ErrorHandling.Aborted;
        }, ct));
        return command;
    }
}
