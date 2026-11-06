using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Templates;

/// <summary>
/// Wording templates stored in one Markdown file per language. A line starting with <c>## </c> starts a template,
/// its text up to the next such line is the body. Placeholders look like <c>{name}</c>; braces followed by anything
/// else (such as the JSON examples in prompts) are plain text.
/// </summary>
public sealed partial class MarkdownTemplates : ITemplateSource
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;
    private readonly Dictionary<string, string> _hashes;

    private MarkdownTemplates(IReadOnlyDictionary<string, string> textByLanguage)
    {
        _sections = textByLanguage.ToDictionary(p => p.Key, p => ParseSections(p.Value), StringComparer.Ordinal);
        _hashes = textByLanguage.ToDictionary(
            p => p.Key,
            p => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Value)))[..16],
            StringComparer.Ordinal);
    }

    /// <summary>Creates templates from texts per language code.</summary>
    public static MarkdownTemplates FromText(IReadOnlyDictionary<string, string> textByLanguage) => new(textByLanguage);

    /// <summary>
    /// Loads embedded resources named <c>{resourcePrefix}.{language}.md</c> for the given languages.
    /// </summary>
    public static MarkdownTemplates FromResources(Assembly assembly, string resourcePrefix, params string[] languages)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            var name = $"{resourcePrefix}.{language}.md";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            texts[language] = reader.ReadToEnd();
        }

        return new MarkdownTemplates(texts);
    }

    /// <inheritdoc />
    public string Render(TemplateRef reference, string language)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_sections.TryGetValue(language, out var sections))
        {
            throw new KeyNotFoundException($"No templates for language '{language}'.");
        }

        if (!sections.TryGetValue(reference.Key, out var body))
        {
            throw new KeyNotFoundException($"Template '{reference.Key}' not found for language '{language}'.");
        }

        return PlaceholderPattern().Replace(body, match =>
        {
            var name = match.Groups[1].Value;
            return reference.Values is not null && reference.Values.TryGetValue(name, out var value)
                ? value
                : throw new KeyNotFoundException($"Template '{reference.Key}' needs a value for '{name}'.");
        });
    }

    /// <inheritdoc />
    public string Hash(string language) =>
        _hashes.TryGetValue(language, out var hash) ? hash : throw new KeyNotFoundException($"No templates for language '{language}'.");

    /// <summary>True if the language has a template with this key.</summary>
    public bool Contains(string language, string key) => _sections.TryGetValue(language, out var s) && s.ContainsKey(key);

    private static Dictionary<string, string> ParseSections(string text)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (key is not null)
            {
                sections[key] = body.ToString().Trim('\n', '\r', ' ');
            }

            body.Clear();
        }

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                key = line[3..].Trim();
            }
            else if (key is not null)
            {
                body.Append(line).Append('\n');
            }
        }

        Flush();
        return sections;
    }

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.]*)\}")]
    private static partial Regex PlaceholderPattern();
}
