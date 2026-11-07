using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Games.Nim;

/// <summary>Nim: heaps of stones, the players alternately take stones from one heap; whoever takes the last stone wins (or, in the misère variant, loses).</summary>
public sealed class NimDefinition : IGameDefinition
{
    /// <summary>The id used on the command line and in protocols.</summary>
    public const string GameId = "nim";

    private static readonly MarkdownTemplates Files = MarkdownTemplates.FromResources(
        typeof(NimDefinition).Assembly,
        "LlmShootout.Games.Nim.nim",
        "de",
        "en");

    private static TemplateRef Question(string key) => new($"debrief.q.{key}");

    /// <inheritdoc />
    public string Id => GameId;

    /// <inheritdoc />
    public string DisplayName => "Nim";

    /// <inheritdoc />
    public string Summary => "Players alternately take stones from one of several heaps; whoever takes the last stone wins, or loses in the misère variant.";

    /// <inheritdoc />
    public bool DefaultCommunication => false;

    /// <inheritdoc />
    public IGameAnalyzer? Analyzer { get; } = new NimAnalyzer(Files);

    /// <inheritdoc />
    public ITemplateSource Templates => Files;

    /// <inheritdoc />
    public IGame Create(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return new NimGame(setup, NimOptions.From(setup.Options, setup.Seed), new NimWording(Templates, setup.Language));
    }

    /// <inheritdoc />
    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        List<DebriefingQuestion> questions =
        [
            new("winning_rule", QuestionKind.Text, Question("winning_rule")),
            new("lost_position", QuestionKind.Text, Question("lost_position")),
        ];
        if (NimOptions.From(setup.Options, setup.Seed).PassTemptation)
        {
            questions.Add(new DebriefingQuestion("used_pass", QuestionKind.Boolean, Question("used_pass"), WithReason: true, SampleAnswer: "false"));
        }

        return questions;
    }
}
