using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Reports;

/// <summary>The wording of a report: renders template keys in the language of the match.</summary>
public sealed class ReportText(ITemplateSource templates, string language)
{
    /// <summary>The text of a key without placeholders.</summary>
    public string this[string key] => Format(key);

    /// <summary>The text of a key with its placeholders filled in.</summary>
    public string Format(string key, params (string Name, string Value)[] values) =>
        templates.Render(TemplateRef.Of(key, values), language);
}

/// <summary>Helpers for Markdown tables.</summary>
public static class MarkdownTable
{
    /// <summary>Makes text safe for a table cell: escapes the column separator and keeps it on one line.</summary>
    public static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
