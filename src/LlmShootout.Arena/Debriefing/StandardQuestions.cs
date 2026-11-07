using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Debriefing;

/// <summary>The questions every game asks after the match. Games add their own through <c>ExtraQuestions</c>.</summary>
public static class StandardQuestions
{
    /// <summary>All standard questions.</summary>
    public static IReadOnlyList<DebriefingQuestion> All { get; } =
    [
        new("strategy", QuestionKind.Text, Ref("strategy")),
        new("decisive_move", QuestionKind.Text, Ref("decisive_move")),
        new("own_mistake", QuestionKind.Text, Ref("own_mistake")),
        new("opponent_strength", QuestionKind.Scale, Ref("opponent_strength"), Min: 1, Max: 5),
        new("rules_followed", QuestionKind.Boolean, Ref("rules_followed"), WithReason: true, SampleAnswer: "true"),
        new("next_time", QuestionKind.Text, Ref("next_time")),
    ];

    /// <summary>The questions that apply to a match: the standard ones first, then the extra ones of the game.</summary>
    public static IReadOnlyList<DebriefingQuestion> For(IEnumerable<DebriefingQuestion> extra) => [.. All.Concat(extra)];

    private static TemplateRef Ref(string key) => new($"debrief.q.{key}");
}
