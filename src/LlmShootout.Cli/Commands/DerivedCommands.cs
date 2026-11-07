using System.CommandLine;
using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout report and debrief: everything that works on stored matches.</summary>
public static class DerivedCommands
{
    /// <summary>Creates the <c>report</c> command.</summary>
    public static Command BuildReport(CliEnvironment env)
    {
        var path = new Argument<string>("directory") { Description = "A match directory or a series directory." };
        var command = new Command("report", "Rebuilds match.json and report.md (and the series files) from the stored protocol.") { path };
        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var directory = parse.GetRequiredValue(path);
            await RebuildAsync(directory, ct);
            env.Console.WriteLine(directory);
            return 0;
        }, ct));
        return command;
    }

    /// <summary>Creates the <c>debrief</c> command.</summary>
    public static Command BuildDebrief(CliEnvironment env)
    {
        var run = new RunOptionSet();
        var path = new Argument<string>("match") { Description = "A match directory." };
        var command = new Command("debrief", "Asks the players again; the answers go to a new file, the protocol stays unchanged.") { path };
        command.Options.Add(run.PlayerA);
        command.Options.Add(run.PlayerB);
        command.Options.Add(run.MaxCost);
        command.Options.Add(run.Temperature);
        command.Options.Add(run.ReasoningEffort);
        command.Options.Add(run.MaxOutputTokens);
        command.Options.Add(run.Timeout);

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var settings = run.Read(parse, env.BaseOptions);
            var record = MatchRecord.Load(parse.GetRequiredValue(path));
            var definition = CliEnvironment.RequireGame(record.GameId);
            var players = new Dictionary<Seat, IPlayer>
            {
                [Seat.A] = await env.CreatePlayerAsync(SpecFor(settings.PlayerA, record, Seat.A), record.Started.Seed + 1, WithRecorded(settings.Endpoint, record, Seat.A, parse, run), ct),
                [Seat.B] = await env.CreatePlayerAsync(SpecFor(settings.PlayerB, record, Seat.B), record.Started.Seed + 2, WithRecorded(settings.Endpoint, record, Seat.B, parse, run), ct),
            };

            var file = $"debrief.{env.Time.GetUtcNow():yyyyMMdd-HHmmss}.jsonl";
            var cost = new CostMeter(settings.Match.MaxCostUsd);
            await using (var writer = new ProtocolWriter(record.Directory, file))
            {
                await new Debriefer(new PlayerGateway(cost), env.Time).RunAsync(definition, record, players, record.Started.Options, writer, ct);
            }

            var events = ProtocolReader.ReadAll(record.Directory, file).OfType<DebriefEvent>().ToArray();
            PrintAnswers(env.Console, events);
            env.Console.WriteLine($"Written to {Path.Combine(record.Directory, file)} (the protocol events.jsonl is unchanged).");
            return events.All(e => e.Valid) ? 0 : ErrorHandling.Aborted;
        }, ct));
        return command;
    }

    /// <summary>Uses the settings of the original match for every setting that was not given on the command line.</summary>
    private static EndpointOptions WithRecorded(EndpointOptions options, MatchRecord record, Seat seat, ParseResult parse, RunOptionSet run)
    {
        var recorded = record.Started.Players[seat].Settings;
        if (recorded is null)
        {
            return options;
        }

        return CliEnvironment.Validate(options with
        {
            Temperature = parse.GetValue(run.Temperature) is null && recorded.TryGetValue(PlayerSettingKeys.Temperature, out var t) ? float.Parse(t, CultureInfo.InvariantCulture) : options.Temperature,
            ReasoningEffort = parse.GetValue(run.ReasoningEffort) is null && recorded.TryGetValue(PlayerSettingKeys.ReasoningEffort, out var e) ? e : options.ReasoningEffort,
            MaxOutputTokens = parse.GetValue(run.MaxOutputTokens) is null && recorded.TryGetValue(PlayerSettingKeys.MaxOutputTokens, out var m) ? int.Parse(m, CultureInfo.InvariantCulture) : options.MaxOutputTokens,
            CallTimeout = parse.GetValue(run.Timeout) is null && recorded.TryGetValue(PlayerSettingKeys.CallTimeoutSeconds, out var c) ? TimeSpan.FromSeconds(double.Parse(c, CultureInfo.InvariantCulture)) : options.CallTimeout,
        });
    }

    private static string SpecFor(string? given, MatchRecord record, Seat seat)
    {
        var name = given ?? record.Started.Players[seat].Name;
        return given is null && name.StartsWith("scripted:", StringComparison.Ordinal)
            ? throw new UsageException($"Seat {seat} was played by a script ({name}). Pass the player again with --{(seat == Seat.A ? "a" : "b")} <player>.")
            : name;
    }

    /// <summary>The match directories below a path: the path itself if it is a match, otherwise its sub-directories that contain a protocol.</summary>
    public static IReadOnlyList<string> MatchDirectories(string path)
    {
        if (File.Exists(Path.Combine(path, ProtocolFiles.Events)))
        {
            return [path];
        }

        if (!Directory.Exists(path))
        {
            throw new UsageException($"'{path}' does not exist.");
        }

        var matches = Directory.EnumerateDirectories(path).Where(d => File.Exists(Path.Combine(d, ProtocolFiles.Events))).Order(StringComparer.Ordinal).ToArray();
        return matches.Length > 0 ? matches : throw new UsageException($"'{path}' contains no match: no {ProtocolFiles.Events} found in it or in its sub-directories.");
    }

    private static async Task RebuildAsync(string path, CancellationToken cancellationToken)
    {
        var records = new List<MatchRecord>();
        foreach (var directory in MatchDirectories(path))
        {
            var record = MatchRecord.Load(directory);
            await MatchReportWriter.WriteAsync(CliEnvironment.RequireGame(record.GameId), record, cancellationToken);
            records.Add(record);
        }

        if (!File.Exists(Path.Combine(path, ProtocolFiles.Events)) && File.Exists(Path.Combine(path, SeriesReportWriter.SummaryFile)))
        {
            var first = records[0];
            var definition = CliEnvironment.RequireGame(first.GameId);
            var summary = SeriesReportWriter.Summarize(definition, first.Started.Players[Seat.A].Name, first.Started.Players[Seat.B].Name, records, first.Started.Language);
            await SeriesReportWriter.WriteAsync(definition, path, summary, first.Started.Language, cancellationToken);
        }
    }

    private static void PrintAnswers(IAnsiConsole console, DebriefEvent[] events)
    {
        var table = new Table().AddColumns("Question", "Seat A", "Seat B");
        var keys = events.SelectMany(e => e.Answers.Keys).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var key in keys)
        {
            table.AddRow(
                Markup.Escape(key),
                Markup.Escape(events.FirstOrDefault(e => e.Seat == Seat.A)?.Answers.GetValueOrDefault(key) ?? "-"),
                Markup.Escape(events.FirstOrDefault(e => e.Seat == Seat.B)?.Answers.GetValueOrDefault(key) ?? "-"));
        }

        console.Write(table);
        foreach (var e in events.Where(e => !e.Valid))
        {
            console.MarkupLine($"[yellow]Debriefing of seat {e.Seat} failed:[/] {Markup.Escape(string.Join("; ", e.Problems))}");
        }

        console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{events.Count(e => e.Valid)} of {events.Length} debriefings valid."));
    }
}
