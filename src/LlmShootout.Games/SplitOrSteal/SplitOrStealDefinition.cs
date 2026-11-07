using LlmShootout.Arena.Annotation;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>Split or Steal: does a model keep its word?</summary>
public sealed class SplitOrStealDefinition : IGameDefinition
{
    /// <summary>The id used on the command line and in protocols.</summary>
    public const string GameId = "split-or-steal";

    /// <summary>Creates the definition with its report analysis and judge task.</summary>
    public SplitOrStealDefinition()
    {
        Analyzer = new SplitOrStealAnalyzer(Templates);
        AnnotationTask = new PromiseAnnotation(Templates);
    }

    /// <inheritdoc />
    public string Id => GameId;

    /// <inheritdoc />
    public string DisplayName => "Split or Steal";

    /// <inheritdoc />
    public string Question => "Does a model keep its word?";

    /// <inheritdoc />
    public ITemplateSource Templates { get; } = MarkdownTemplates.FromResources(
        typeof(SplitOrStealDefinition).Assembly,
        "LlmShootout.Games.SplitOrSteal.split-or-steal",
        "de",
        "en");

    /// <inheritdoc />
    public IGameAnalyzer? Analyzer { get; }

    /// <inheritdoc />
    public IAnnotationTask? AnnotationTask { get; }

    /// <inheritdoc />
    public IGame Create(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return new SplitOrStealGame(setup, SplitOrStealOptions.From(setup.Options));
    }

    /// <inheritdoc />
    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return setup.Communication
            ?
            [
                new DebriefingQuestion(
                    "announced_truthfully",
                    QuestionKind.Boolean,
                    new TemplateRef("question.announced_truthfully"),
                    WithReason: true),
            ]
            : [];
    }
}
