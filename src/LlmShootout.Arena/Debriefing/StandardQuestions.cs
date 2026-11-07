using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Debriefing;

/// <summary>The questions every game asks after the match. Games add their own through <c>ExtraQuestions</c>.</summary>
public static class StandardQuestions
{
    /// <summary>All standard questions.</summary>
    public static IReadOnlyList<DebriefingQuestion> All { get; } =
    [
        new("strategy", QuestionKind.Text, Ref("strategy")),
        new("opponent_strategy", QuestionKind.Text, Ref("opponent_strategy")),
        new("trust_in_opponent", QuestionKind.Scale, Ref("trust_in_opponent"), Min: 1, Max: 5),
        new("tried_to_deceive", QuestionKind.Boolean, Ref("tried_to_deceive"), WithReason: true),
        new("next_time", QuestionKind.Text, Ref("next_time")),
    ];

    /// <summary>The questions that apply to a match: the standard ones first, then the extra ones of the game.</summary>
    public static IReadOnlyList<DebriefingQuestion> For(IEnumerable<DebriefingQuestion> extra) => [.. All.Concat(extra)];

    private static TemplateRef Ref(string key) => new($"debrief.q.{key}");
}
