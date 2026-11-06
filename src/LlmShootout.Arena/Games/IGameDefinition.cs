using System.Text.Json.Nodes;
using LlmShootout.Arena.Debriefing;

namespace LlmShootout.Arena.Games;

/// <summary>Everything the arena needs to know about a game before a match starts.</summary>
public interface IGameDefinition
{
    /// <summary>Stable id used on the command line and in protocols, for example <c>split-or-steal</c>.</summary>
    string Id { get; }

    /// <summary>Name for people.</summary>
    string DisplayName { get; }

    /// <summary>All wording of the game: rules, views, instructions, error messages, facts.</summary>
    ITemplateSource Templates { get; }

    /// <summary>
    /// Creates a game. Validates <see cref="GameSetup.Options"/> and throws <see cref="GameOptionsException"/>
    /// with a helpful message if they are wrong.
    /// </summary>
    IGame Create(GameSetup setup);

    /// <summary>Questions of the debriefing that are specific to this game.</summary>
    IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup);

    /// <summary>Game specific parts of match and series reports, or null if the generic report is enough.</summary>
    IGameAnalyzer? Analyzer { get; }
}

/// <summary>Everything that varies between two matches of the same game.</summary>
/// <param name="Seed">Seed for all random decisions of the game.</param>
/// <param name="Communication">True if players may talk to each other.</param>
/// <param name="Language">Prompt language, <c>de</c> or <c>en</c>.</param>
/// <param name="FirstSeat">The seat that starts when the game has an order.</param>
/// <param name="Options">Game specific options from the series file (pot size, rounds, roles), or null for defaults.</param>
public sealed record GameSetup(
    long Seed,
    bool Communication,
    string Language,
    Seat FirstSeat = Seat.A,
    JsonObject? Options = null);

/// <summary>The options of a game were invalid.</summary>
public sealed class GameOptionsException : Exception
{
    /// <summary>Creates the exception with a message for the person who wrote the series file.</summary>
    public GameOptionsException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public GameOptionsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public GameOptionsException()
    {
    }
}

/// <summary>Turns a <see cref="TemplateRef"/> into text in a language and identifies the template version.</summary>
public interface ITemplateSource
{
    /// <summary>Renders a template. Throws if the key or a placeholder value is missing.</summary>
    string Render(TemplateRef reference, string language);

    /// <summary>A hash over the template file of a language, recorded in the protocol.</summary>
    string Hash(string language);
}
