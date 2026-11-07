using System.CommandLine;
using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Reports;
using LlmShootout.Arena.Series;
using LlmShootout.Arena.Templates;
using LlmShootout.Cli.Output;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout play: one match.</summary>
public static class PlayCommand
{
    /// <summary>Creates the command.</summary>
    public static Command Build(CliEnvironment env)
    {
        var run = new RunOptionSet();
        var game = new Argument<string>("game") { Description = "Game id, see: shootout games" };
        var communication = new Option<string?>("--communication") { Description = "Messages between players: on or off. Default: the setting of the game, see: shootout games." };
        communication.AcceptOnlyFromAmong("on", "off");
        var seed = new Option<long?>("--seed") { Description = "Seed for all random decisions. Default: derived from the clock; it is printed and stored." };
        var first = new Option<string?>("--first-seat") { Description = "Seat that starts, A or B. Default: the seed decides." };
        var gameOption = new Option<string[]>("--game-option") { Description = "Game specific option as key=value, repeatable (for example pot=1000).", AllowMultipleArgumentsPerToken = false };

        var command = new Command("play", "Plays one match between two players.") { game, communication, seed, first, gameOption };
        foreach (var option in run.All)
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var settings = run.Read(parse, env.BaseOptions);
            var definition = CliEnvironment.RequireGame(parse.GetRequiredValue(game));
            var (specA, specB) = RequirePlayers(settings);
            var matchSeed = parse.GetValue(seed) ?? env.Time.GetUtcNow().Ticks % int.MaxValue;
            var firstSeat = parse.GetValue(first) is { } f ? ParseSeat(f) : new SeededRng(matchSeed).NextBool(0.5) ? Seat.A : Seat.B;
            var setup = new GameSetup(
                matchSeed,
                definition.ResolveCommunication(parse.GetValue(communication) is { } requested ? requested == "on" : null),
                settings.Language ?? ArenaTemplates.DefaultLanguage,
                firstSeat,
                RunOptionSet.ParseGameOptions(parse.GetValue(gameOption)));

            var cost = new CostMeter(settings.Match.MaxCostUsd);
            var players = new Dictionary<Seat, IPlayer>
            {
                [Seat.A] = await env.CreatePlayerAsync(specA, matchSeed + 1, settings.Endpoint, ct),
                [Seat.B] = await env.CreatePlayerAsync(specB, matchSeed + 2, settings.Endpoint, ct),
            };
            var directory = MatchPaths.Match(settings.OutDirectory, env.Time.GetUtcNow(), definition.Id, players[Seat.A].Name, players[Seat.B].Name);
            var plan = new MatchPlan(definition, setup, players, settings.Match, directory);

            var view = new LiveView(env.Console, settings.ShowThoughts, cost);
            var runner = env.CreateRunner(cost, settings.Quiet ? null : view.Handle);
            var outcome = await runner.RunAsync(plan, ct);

            var record = MatchRecord.Load(outcome.Directory);
            await MatchReportWriter.WriteAsync(definition, record, ct);
            Finish(env, settings, outcome);
            if (outcome.AbortReason is not null && settings.Quiet)
            {
                env.Error.MarkupLine($"[yellow]Aborted:[/] {Markup.Escape(outcome.AbortReason)}: {Markup.Escape(outcome.AbortDetail ?? string.Empty)}");
            }

            return outcome.AbortReason is null ? 0 : ErrorHandling.Aborted;
        }, ct));
        return command;
    }

    private static Seat ParseSeat(string text) =>
        Enum.TryParse<Seat>(text.Trim(), ignoreCase: true, out var seat) && Enum.IsDefined(seat)
            ? seat
            : throw new UsageException($"--first-seat must be A or B, but is '{text}'.");

    internal static (string A, string B) RequirePlayers(RunSettings settings) =>
        (settings.PlayerA ?? throw new UsageException("Please give both players: --a <player> --b <player>. Use 'random' to play without an API key."),
         settings.PlayerB ?? throw new UsageException("Please give both players: --a <player> --b <player>. Use 'random' to play without an API key."));

    private static void Finish(CliEnvironment env, RunSettings settings, MatchOutcome outcome)
    {
        if (settings.Quiet)
        {
            env.Console.WriteLine(outcome.Directory);
            return;
        }

        env.Console.WriteLine();
        env.Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Report: {Path.Combine(outcome.Directory, ProtocolFiles.Report)}"));
    }
}
