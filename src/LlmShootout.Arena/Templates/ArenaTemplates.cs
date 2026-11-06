namespace LlmShootout.Arena.Templates;

/// <summary>The wording the arena itself needs: system prompt, headings, field rules, retry hints.</summary>
public static class ArenaTemplates
{
    /// <summary>Languages for which prompts exist.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["de", "en"];

    /// <summary>The arena templates, loaded from embedded resources.</summary>
    public static MarkdownTemplates Default { get; } = MarkdownTemplates.FromResources(
        typeof(ArenaTemplates).Assembly,
        "LlmShootout.Arena.Prompts.arena",
        "de",
        "en");
}
