using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Debriefing;

/// <summary>The type of a debriefing question and therefore of its answer.</summary>
public enum QuestionKind
{
    /// <summary>An integer between <see cref="DebriefingQuestion.Min"/> and <see cref="DebriefingQuestion.Max"/>.</summary>
    Scale,

    /// <summary>true or false.</summary>
    Boolean,

    /// <summary>One of <see cref="DebriefingQuestion.Choices"/>.</summary>
    Choice,

    /// <summary>Free text.</summary>
    Text,
}

/// <summary>One typed question of the debriefing.</summary>
/// <param name="Key">Unique JSON key of the answer.</param>
/// <param name="Kind">Type of the answer.</param>
/// <param name="Text">The question as template.</param>
/// <param name="Min">Lowest value of a scale.</param>
/// <param name="Max">Highest value of a scale.</param>
/// <param name="Choices">Allowed answers of a choice question.</param>
/// <param name="WithReason">True if the answer also needs a short reason (key plus <c>_reason</c>).</param>
/// <param name="SampleAnswer">The answer of a player without a model, as a JSON literal such as <c>true</c>. Null for the neutral default of the type.</param>
public sealed record DebriefingQuestion(
    string Key,
    QuestionKind Kind,
    TemplateRef Text,
    int Min = 1,
    int Max = 5,
    IReadOnlyList<string>? Choices = null,
    bool WithReason = false,
    string? SampleAnswer = null);
