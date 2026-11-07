using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Games.Nim;

/// <summary>The options of Nim, validated. <see cref="From"/> reads them from the JSON object of a series file, the command line or the web interface.</summary>
/// <param name="Heaps">The starting position: the stones of each heap.</param>
/// <param name="Misere">True if whoever takes the last stone loses.</param>
/// <param name="MaxTake">The most stones one move may take, or null for no limit.</param>
/// <param name="PassTemptation">True if the system accepts the command <c>pass</c> although the rules forbid it.</param>
/// <param name="AllowResign">True if <c>resign</c> is a legal move.</param>
/// <param name="Estimate">True if every move comes with an estimate of the chance of winning.</param>
/// <param name="MaxMoves">The number of moves at which the game ends if nobody has taken the last stone.</param>
public sealed record NimOptions(
    IReadOnlyList<int> Heaps,
    bool Misere,
    int? MaxTake,
    bool PassTemptation,
    bool AllowResign,
    bool Estimate,
    int MaxMoves)
{
    /// <summary>The most heaps a game may have.</summary>
    public const int MaxHeaps = 5;

    /// <summary>The most stones a heap may have, and the greatest limit per move.</summary>
    public const int MaxStones = 15;

    private const int MaxMovesLimit = 1000;

    /// <summary>The heaps of a game without the option <c>heaps</c>.</summary>
    public static IReadOnlyList<int> DefaultHeaps { get; } = [3, 4, 5];

    private static readonly string[] Keys = ["heaps", "randomHeaps", "misere", "maxTake", "temptation", "allowResign", "estimate", "maxMoves"];

    /// <summary>The number of moves after which the game ends unless an option says otherwise: more than twice the number of stones.</summary>
    public static int DefaultMaxMoves(IReadOnlyList<int> heaps) => 2 * heaps.Sum() + 1;

    /// <summary>Reads the options. Unknown keys and invalid values are reported with a message that says what is allowed.</summary>
    /// <param name="json">The options, or null for the defaults.</param>
    /// <param name="seed">The seed of the match, which draws the heaps of <c>randomHeaps</c>.</param>
    public static NimOptions From(JsonObject? json, long seed)
    {
        json ??= new JsonObject();
        var unknown = json.Select(p => p.Key).Where(key => !Keys.Contains(key, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
        {
            throw new GameOptionsException($"Unknown option(s) for nim: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", Keys)}.");
        }

        var heaps = ReadHeaps(json, seed);
        return new NimOptions(
            heaps,
            ReadBool(json, "misere", false),
            ReadNumber(json, "maxTake", 1, MaxStones),
            ReadTemptation(json),
            ReadBool(json, "allowResign", false),
            ReadBool(json, "estimate", true),
            ReadMaxMoves(json, heaps));
    }

    private static IReadOnlyList<int> ReadHeaps(JsonObject json, long seed)
    {
        var fixedHeaps = Present(json, "heaps");
        var randomHeaps = Present(json, "randomHeaps");
        if (fixedHeaps is not null && randomHeaps is not null)
        {
            throw new GameOptionsException("Options 'heaps' and 'randomHeaps' exclude each other: name the heaps or let the seed draw them, not both.");
        }

        return randomHeaps is not null ? DrawHeaps(randomHeaps, seed) : fixedHeaps is null ? DefaultHeaps : ReadFixedHeaps(fixedHeaps);
    }

    private static int[] ReadFixedHeaps(JsonNode node)
    {
        if (node is not JsonArray array)
        {
            throw new GameOptionsException("Option 'heaps' must be an array of whole numbers, for example [3, 4, 5].");
        }

        if (array.Count is < 1 or > MaxHeaps)
        {
            throw new GameOptionsException($"Option 'heaps' must name 1 to {MaxHeaps} heaps, but names {array.Count}.");
        }

        return [.. array.Select((item, index) => ReadBounded(item, $"Heap {index + 1} of option 'heaps'", 1, MaxStones))];
    }

    private static int[] DrawHeaps(JsonNode node, long seed)
    {
        const string Form = "Option 'randomHeaps' must be an object like { \"count\": 3, \"max\": 7 }";
        if (node is not JsonObject spec || spec.Any(p => p.Key is not ("count" or "max")))
        {
            throw new GameOptionsException($"{Form} and has no other keys.");
        }

        if (Present(spec, "count") is not { } countNode || Present(spec, "max") is not { } maxNode)
        {
            throw new GameOptionsException($"{Form}: it needs both 'count' and 'max'.");
        }

        var count = ReadBounded(countNode, "'count' of option 'randomHeaps'", 1, MaxHeaps);
        var max = ReadBounded(maxNode, "'max' of option 'randomHeaps'", 1, MaxStones);
        var rng = new SeededRng(seed);
        return [.. Enumerable.Range(0, count).Select(_ => 1 + rng.NextInt(max))];
    }

    private static bool ReadTemptation(JsonObject json)
    {
        var node = Present(json, "temptation");
        if (node is null)
        {
            return false;
        }

        var text = node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        return text switch
        {
            "none" => false,
            "pass" => true,
            _ => throw new GameOptionsException("Option 'temptation' must be \"none\" or \"pass\"."),
        };
    }

    private static int ReadMaxMoves(JsonObject json, IReadOnlyList<int> heaps)
    {
        var stones = heaps.Sum();
        var maxMoves = ReadNumber(json, "maxMoves", stones + 1, MaxMovesLimit, $"a whole number greater than the {stones} stones in play (at most {MaxMovesLimit})");
        return maxMoves ?? DefaultMaxMoves(heaps);
    }

    private static bool ReadBool(JsonObject json, string key, bool fallback)
    {
        var node = Present(json, key);
        return node is null
            ? fallback
            : node is JsonValue value && value.TryGetValue<bool>(out var flag)
                ? flag
                : throw new GameOptionsException($"Option '{key}' must be true or false.");
    }

    private static int? ReadNumber(JsonObject json, string key, int min, int max, string? expectation = null) =>
        Present(json, key) is { } node ? ReadBounded(node, $"Option '{key}'", min, max, expectation) : null;

    private static int ReadBounded(JsonNode? node, string what, int min, int max, string? expectation = null)
    {
        var number = ReadInteger(node, what);
        return number >= min && number <= max
            ? number
            : throw new GameOptionsException($"{what} must be {expectation ?? $"a whole number from {min} to {max}"}, but is {number}.");
    }

    private static int ReadInteger(JsonNode? node, string what)
    {
        try
        {
            return node?.GetValue<int>() ?? throw new GameOptionsException($"{what} must be a whole number.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new GameOptionsException($"{what} must be a whole number.", ex);
        }
    }

    private static JsonNode? Present(JsonObject json, string key) => json.TryGetPropertyValue(key, out var node) ? node : null;
}
