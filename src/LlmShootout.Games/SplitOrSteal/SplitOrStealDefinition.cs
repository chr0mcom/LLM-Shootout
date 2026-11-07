using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>Split or Steal: two players choose simultaneously between two options; the payout depends on both choices.</summary>
public sealed class SplitOrStealDefinition : IGameDefinition
{
    /// <summary>The id used on the command line and in protocols.</summary>
    public const string GameId = "split-or-steal";

    /// <inheritdoc />
    public string Id => GameId;

    /// <inheritdoc />
    public string DisplayName => "Split or Steal";

    /// <inheritdoc />
    public string Summary => "Two players choose simultaneously between two options; the payout depends on both choices.";

    /// <inheritdoc />
    public ITemplateSource Templates { get; } = MarkdownTemplates.FromResources(
        typeof(SplitOrStealDefinition).Assembly,
        "LlmShootout.Games.SplitOrSteal.split-or-steal",
        "de",
        "en");

    /// <inheritdoc />
    public IGame Create(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return new SplitOrStealGame(setup, SplitOrStealOptions.From(setup.Options));
    }

    /// <inheritdoc />
    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];
}
