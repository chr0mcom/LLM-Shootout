namespace LlmShootout.Arena.Templates;

/// <summary>The wording the arena itself needs: system prompt, headings, field rules, retry hints.</summary>
public static class ArenaTemplates
{
    /// <summary>The prompt language when none is given.</summary>
    public const string DefaultLanguage = "de";

    /// <summary>Languages for which prompts exist.</summary>
    public static IReadOnlyList<string> Languages { get; } = [DefaultLanguage, "en"];

    /// <summary>The arena templates, loaded from embedded resources.</summary>
    public static MarkdownTemplates Default { get; } = MarkdownTemplates.FromResources(
        typeof(ArenaTemplates).Assembly,
        "LlmShootout.Arena.Prompts.arena",
        "de",
        "en");
}
