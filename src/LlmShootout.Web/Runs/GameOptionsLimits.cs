using System.Text;
using System.Text.Json.Nodes;

namespace LlmShootout.Web.Runs;

/// <summary>Bounds the game options of a start request in size and depth, because the browser may send anything.</summary>
internal static class GameOptionsLimits
{
    public const int MaxBytes = 2048;
    public const int MaxDepth = 4;

    /// <summary>Returns the options, or null if there are none (null or an empty object). Throws <see cref="StartRejectedException"/> if they are too large or too deep.</summary>
    public static JsonObject? Check(JsonObject? options)
    {
        if (options is null || options.Count == 0)
        {
            return null;
        }

        if (DepthOf(options) > MaxDepth)
        {
            throw new StartRejectedException($"The game options are nested too deeply (at most {MaxDepth} levels).");
        }

        if (Encoding.UTF8.GetByteCount(options.ToJsonString()) > MaxBytes)
        {
            throw new StartRejectedException($"The game options are too large (at most {MaxBytes} bytes).");
        }

        return options;
    }

    private static int DepthOf(JsonNode? node) => node switch
    {
        JsonObject o => 1 + o.Select(p => DepthOf(p.Value)).DefaultIfEmpty(0).Max(),
        JsonArray a => 1 + a.Select(DepthOf).DefaultIfEmpty(0).Max(),
        _ => 0,
    };
}
