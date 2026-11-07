using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Series;

/// <summary>One match of a series.</summary>
/// <param name="Communication">True or false for messages between players. If absent, the default of the game applies.</param>
/// <param name="FirstSeat">The seat that starts (<c>A</c> or <c>B</c>). If absent, the seed decides.</param>
/// <param name="Options">Game specific options.</param>
public sealed record SeriesMatch(bool? Communication = null, Seat? FirstSeat = null, JsonObject? Options = null);

/// <summary>
/// A series: the same pairing over several matches with defined variants. A rematch with other models or other
/// conditions is another file or other command line options, never other code.
/// </summary>
/// <param name="Game">Id of the game.</param>
/// <param name="Seed">Seed of the series; match <c>i</c> uses <c>Seed + i</c>.</param>
/// <param name="Matches">The matches in order.</param>
/// <param name="Language">Prompt language; if absent the command line default applies.</param>
/// <param name="PlayerA">Player for seat A; may be overridden on the command line.</param>
/// <param name="PlayerB">Player for seat B; may be overridden on the command line.</param>
public sealed record SeriesFile(string Game, long Seed, IReadOnlyList<SeriesMatch> Matches, string? Language = null, string? PlayerA = null, string? PlayerB = null)
{
    /// <summary>Reads and validates a series file. Errors name the file and the problem.</summary>
    public static SeriesFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new SeriesFileException($"Series file '{path}' does not exist.");
        }

        SeriesFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(path), SeriesJson.Default.SeriesFile);
        }
        catch (JsonException ex)
        {
            throw new SeriesFileException($"Series file '{path}' is not valid: {ex.Message}", ex);
        }

        if (file is null || string.IsNullOrWhiteSpace(file.Game) || file.Matches is null || file.Matches.Count == 0)
        {
            throw new SeriesFileException($"Series file '{path}' needs a 'game' and at least one entry in 'matches'.");
        }

        return file;
    }
}

/// <summary>A series file is missing or invalid.</summary>
public sealed class SeriesFileException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SeriesFileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public SeriesFileException()
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public SeriesFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Source-generated JSON settings for series files: camelCase, comments and trailing commas allowed, unknown fields
/// are errors (so that <c>"firstSet": "A"</c> is noticed), and <c>game</c>, <c>seed</c> and <c>matches</c> are required.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    WriteIndented = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeriesFile))]
public sealed partial class SeriesJson : JsonSerializerContext
{
}
