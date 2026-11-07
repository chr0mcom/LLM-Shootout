using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using LlmShootout.Arena;
using LlmShootout.Arena.ModelAccess;

namespace LlmShootout.Cli.Commands;

/// <summary>The options that <c>play</c> and <c>series</c> share, and how they map to the settings of a run.</summary>
public sealed class RunOptionSet
{
    /// <summary>Player for seat A.</summary>
    public Option<string?> PlayerA { get; } = new("--a") { Description = "Player for seat A: random, scripted:<file>, <endpoint>:<model> (a configured endpoint), openrouter:<slug> or <slug>." };

    /// <summary>Player for seat B.</summary>
    public Option<string?> PlayerB { get; } = new("--b") { Description = "Player for seat B." };

    /// <summary>Prompt language.</summary>
    public Option<string?> Language { get; } = new Option<string?>("--lang") { Description = "Prompt language: de (default) or en." }.AcceptOnlyFromAmong("de", "en");

    /// <summary>Spending limit.</summary>
    public Option<decimal> MaxCost { get; } = new("--max-cost") { Description = "Stop cleanly when this many USD are spent in this run.", DefaultValueFactory = _ => 1m };

    /// <summary>Skip the debriefing.</summary>
    public Option<bool> NoDebrief { get; } = new("--no-debrief") { Description = "Do not debrief the players after the game." };

    /// <summary>Output directory.</summary>
    public Option<string> Out { get; } = new("--out") { Description = "Directory for match and series directories.", DefaultValueFactory = _ => "matches" };

    /// <summary>Show private thoughts in the live view.</summary>
    public Option<bool> ShowThoughts { get; } = new("--show-thoughts") { Description = "Also print the private thoughts of the players." };

    /// <summary>Quiet mode.</summary>
    public Option<bool> Quiet { get; } = new("--quiet") { Description = "Print only the resulting directory." };

    /// <summary>Redact identity mentions.</summary>
    public Option<bool> RedactIdentity { get; } = new("--redact-identity") { Description = "Replace mentions of model names in public messages by a placeholder (default: only mark them)." };

    /// <summary>Thought memory.</summary>
    public Option<int?> ThoughtMemory { get; } = new("--thought-memory") { Description = "How many of its own earlier thoughts a player sees again (default 10, 0 for none)." };

    /// <summary>Sampling temperature.</summary>
    public Option<float?> Temperature { get; } = new("--temperature") { Description = "Sampling temperature. Not sent unless given." };

    /// <summary>Reasoning effort.</summary>
    public Option<string?> ReasoningEffort { get; } = new Option<string?>("--reasoning-effort") { Description = "Reasoning effort: minimal, low, medium or high. Not sent unless given." }.AcceptOnlyFromAmong("minimal", "low", "medium", "high");

    /// <summary>Output token limit.</summary>
    public Option<int?> MaxOutputTokens { get; } = new("--max-output-tokens") { Description = "Limit of output tokens per call. Not sent unless given." };

    /// <summary>Call timeout.</summary>
    public Option<int?> Timeout { get; } = new("--timeout") { Description = "Seconds allowed for one model call (default 180)." };

    /// <summary>All options, for adding to a command.</summary>
    public IEnumerable<Option> All =>
    [
        PlayerA, PlayerB, Language, MaxCost, NoDebrief, Out, ShowThoughts, Quiet, RedactIdentity, ThoughtMemory,
        Temperature, ReasoningEffort, MaxOutputTokens, Timeout,
    ];

    /// <summary>Reads the options of a parsed command line.</summary>
    public RunSettings Read(ParseResult parse, EndpointOptions baseOptions)
    {
        var options = baseOptions with
        {
            Temperature = parse.GetValue(Temperature) ?? baseOptions.Temperature,
            ReasoningEffort = parse.GetValue(ReasoningEffort) ?? baseOptions.ReasoningEffort,
            MaxOutputTokens = parse.GetValue(MaxOutputTokens) ?? baseOptions.MaxOutputTokens,
            CallTimeout = parse.GetValue(Timeout) is { } seconds ? TimeSpan.FromSeconds(seconds) : baseOptions.CallTimeout,
        };
        var maxCost = parse.GetValue(MaxCost);
        var match = new MatchOptions
        {
            Debrief = !parse.GetValue(NoDebrief),
            MaxCostUsd = maxCost,
            RedactIdentity = parse.GetValue(RedactIdentity),
            ThoughtMemory = parse.GetValue(ThoughtMemory) ?? new MatchOptions().ThoughtMemory,
        };
        return new RunSettings(
            CliEnvironment.Validate(options),
            match,
            parse.GetValue(PlayerA),
            parse.GetValue(PlayerB),
            parse.GetValue(Language),
            parse.GetValue(Out) ?? "matches",
            parse.GetValue(ShowThoughts),
            parse.GetValue(Quiet));
    }

    /// <summary>Turns <c>key=value</c> pairs into a JSON object: integers, booleans and text are recognized.</summary>
    public static JsonObject? ParseGameOptions(IEnumerable<string>? pairs)
    {
        var list = pairs?.ToArray() ?? [];
        if (list.Length == 0)
        {
            return null;
        }

        var json = new JsonObject();
        foreach (var pair in list)
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                throw new UsageException($"Game option '{pair}' must look like key=value, for example --game-option pot=1000.");
            }

            var (key, value) = (pair[..split].Trim(), pair[(split + 1)..].Trim());
            json[key] = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number
                : bool.TryParse(value, out var flag) ? flag
                : value;
        }

        return json;
    }
}

/// <summary>The settings of one run, read from the command line.</summary>
public sealed record RunSettings(
    EndpointOptions Endpoint,
    MatchOptions Match,
    string? PlayerA,
    string? PlayerB,
    string? Language,
    string OutDirectory,
    bool ShowThoughts,
    bool Quiet);
