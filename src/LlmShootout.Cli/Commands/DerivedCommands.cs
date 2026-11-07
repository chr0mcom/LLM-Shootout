using System.CommandLine;
using System.Globalization;
using LlmShootout.Arena;
using LlmShootout.Arena.Annotation;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout report, debrief and annotate: everything that works on stored matches.</summary>
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
            var cost = new CostMeter(settings.MaxCostUsd);
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

    /// <summary>Creates the <c>annotate</c> command.</summary>
    public static Command BuildAnnotate(CliEnvironment env)
    {
        var run = new RunOptionSet();
        var path = new Argument<string>("directory") { Description = "A match directory or a series directory." };
        var judge = new Option<string>("--judge") { Description = "Judge model: <endpoint>:<model>, openrouter:<slug>, <slug>, random or scripted:<file>.", Required = true };
        var command = new Command("annotate", "Lets a judge model rate the matches (for example: was a promise made?). The result goes to its own file and is marked as a derived assessment.") { path, judge };
        command.Options.Add(run.MaxCost);
        command.Options.Add(run.Temperature);
        command.Options.Add(run.ReasoningEffort);
        command.Options.Add(run.MaxOutputTokens);
        command.Options.Add(run.Timeout);

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var settings = run.Read(parse, env.BaseOptions);
            var directory = parse.GetRequiredValue(path);
            var player = await env.CreatePlayerAsync(parse.GetRequiredValue(judge), 1, settings.Endpoint, ct);
            var annotator = new Annotator(new PlayerGateway(new CostMeter(settings.MaxCostUsd)), env.Time);
            foreach (var matchDirectory in MatchDirectories(directory))
            {
                var record = MatchRecord.Load(matchDirectory);
                var task = CliEnvironment.RequireGame(record.GameId).AnnotationTask
                    ?? throw new UsageException($"The game '{record.GameId}' has no judge task.");
                env.Console.WriteLine(await annotator.RunAsync(task, record, player, ct) ?? $"{Path.GetFileName(matchDirectory)}: nothing to rate.");
            }

            await RebuildAsync(directory, ct);
            return 0;
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
            Temperature = parse.GetValue(run.Temperature) is null && recorded.TryGetValue("temperature", out var t) ? float.Parse(t, CultureInfo.InvariantCulture) : options.Temperature,
            ReasoningEffort = parse.GetValue(run.ReasoningEffort) is null && recorded.TryGetValue("reasoningEffort", out var e) ? e : options.ReasoningEffort,
            MaxOutputTokens = parse.GetValue(run.MaxOutputTokens) is null && recorded.TryGetValue("maxOutputTokens", out var m) ? int.Parse(m, CultureInfo.InvariantCulture) : options.MaxOutputTokens,
            CallTimeout = parse.GetValue(run.Timeout) is null && recorded.TryGetValue("callTimeoutSeconds", out var c) ? TimeSpan.FromSeconds(double.Parse(c, CultureInfo.InvariantCulture)) : options.CallTimeout,
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
            var summary = SeriesReportWriter.Summarize(first.GameId, first.Started.Players[Seat.A].Name, first.Started.Players[Seat.B].Name, records);
            await SeriesReportWriter.WriteAsync(CliEnvironment.RequireGame(first.GameId), path, summary, records, first.Started.Language, cancellationToken);
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
