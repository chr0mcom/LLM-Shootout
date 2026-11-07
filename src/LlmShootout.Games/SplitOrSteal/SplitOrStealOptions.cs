using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>Options of Split or Steal. All have defaults, so a series file may omit them.</summary>
/// <param name="Pot">Points in the pot. Must be even and at least 2.</param>
/// <param name="MessagesPerPlayer">Messages each player may send in the talking phase (only with communication).</param>
public sealed record SplitOrStealOptions(int Pot = SplitOrStealOptions.DefaultPot, int MessagesPerPlayer = SplitOrStealOptions.DefaultMessagesPerPlayer)
{
    /// <summary>Points in the pot unless an option says otherwise.</summary>
    public const int DefaultPot = 100;

    /// <summary>Messages each player may write unless an option says otherwise.</summary>
    public const int DefaultMessagesPerPlayer = 3;

    /// <summary>Reads the options of a series file entry. Unknown keys and invalid values are reported with a helpful message.</summary>
    public static SplitOrStealOptions From(JsonObject? json)
    {
        if (json is null)
        {
            return new SplitOrStealOptions();
        }

        var unknown = json.Select(p => p.Key).Where(k => k is not ("pot" or "messagesPerPlayer")).ToArray();
        if (unknown.Length > 0)
        {
            throw new GameOptionsException($"Unknown option(s) for split-or-steal: {string.Join(", ", unknown)}. Allowed: pot, messagesPerPlayer.");
        }

        var options = new SplitOrStealOptions(Read(json, "pot", DefaultPot), Read(json, "messagesPerPlayer", DefaultMessagesPerPlayer));
        if (options.Pot < 2 || options.Pot % 2 != 0)
        {
            throw new GameOptionsException($"Option 'pot' must be an even number of at least 2, but is {options.Pot}.");
        }

        if (options.MessagesPerPlayer is < 0 or > 10)
        {
            throw new GameOptionsException($"Option 'messagesPerPlayer' must be between 0 and 10, but is {options.MessagesPerPlayer}.");
        }

        return options;
    }

    private static int Read(JsonObject json, string key, int fallback)
    {
        if (!json.TryGetPropertyValue(key, out var node) || node is null)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new GameOptionsException($"Option '{key}' must be an integer.", ex);
        }
    }
}
