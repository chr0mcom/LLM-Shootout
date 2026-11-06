namespace LlmShootout.Arena.Answers;

/// <summary>Recognizes answers in which a model declines to play or moralizes instead of moving.</summary>
public static class RefusalDetector
{
    /// <summary>Default keyword list in English and German, matched case-insensitively as substrings.</summary>
    public static IReadOnlyList<string> DefaultKeywords { get; } =
    [
        "i can't", "i cannot", "i can not", "i won't", "i will not", "i'm sorry", "i am sorry", "i'm not able",
        "i am not able", "i am unable", "i'm unable", "i refuse", "i decline", "as an ai", "not comfortable",
        "against my", "unethical", "i must decline",
        "ich kann nicht", "ich werde nicht", "es tut mir leid", "tut mir leid", "als ki", "ich lehne ab",
        "ich verweigere", "nicht in der lage", "unethisch", "ich möchte nicht", "ich darf nicht",
    ];

    /// <summary>True if the text contains one of the keywords.</summary>
    public static bool IsRefusal(string? text, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace('’', '\'');
        return keywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
