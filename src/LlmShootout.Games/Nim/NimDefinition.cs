using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Games.Nim;

/// <summary>Nim: heaps of stones, the players alternately take stones from one heap; whoever takes the last stone wins (or, in the misère variant, loses).</summary>
public sealed class NimDefinition : IGameDefinition
{
    /// <summary>The id used on the command line and in protocols.</summary>
    public const string GameId = "nim";

    /// <inheritdoc />
    public string Id => GameId;

    /// <inheritdoc />
    public string DisplayName => "Nim";

    /// <inheritdoc />
    public string Summary => "Players alternately take stones from one of several heaps; whoever takes the last stone wins, or loses in the misère variant.";

    /// <inheritdoc />
    public bool DefaultCommunication => false;

    /// <inheritdoc />
    public ITemplateSource Templates { get; } = MarkdownTemplates.FromResources(
        typeof(NimDefinition).Assembly,
        "LlmShootout.Games.Nim.nim",
        "de",
        "en");

    /// <inheritdoc />
    public IGame Create(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return new NimGame(setup, NimOptions.From(setup.Options, setup.Seed), new NimWording(Templates, setup.Language));
    }

    /// <inheritdoc />
    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];
}
