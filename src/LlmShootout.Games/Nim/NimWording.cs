using System.Globalization;
using LlmShootout.Arena.Games;

namespace LlmShootout.Games.Nim;

/// <summary>
/// Composes the texts of Nim from the template file: the rules and the view depend on the options, so their parts come from
/// separate templates. The wording stays in the file, in both languages.
/// </summary>
/// <param name="templates">The templates of the game.</param>
/// <param name="language">The language of the match.</param>
internal sealed class NimWording(ITemplateSource templates, string language)
{
    private const char Stone = '●';
    private const int GroupSize = 5;

    /// <summary>The rules, which every seat sees once. They do not name the starting position, which the first view and the first fact show.</summary>
    public TemplateRef Rules(NimOptions options) => TemplateRef.Of("rules", ("details", string.Join('\n', RuleLines(options))));

    /// <summary>The position as the player to move sees it: the heaps and the options in force.</summary>
    public TemplateRef View(IReadOnlyList<int> heaps, NimOptions options, bool ownMove, bool over) =>
        TemplateRef.Of(
            over ? "view.over" : ownMove ? "view.self" : "view.other",
            ("heaps", Heaps(heaps)),
            ("options", string.Join("; ", OptionLines(options))));

    /// <summary>The heaps as lines of stones, for example <c>Heap 1: ●●● (3)</c>.</summary>
    public string Heaps(IReadOnlyList<int> heaps) =>
        string.Join('\n', heaps.Select((stones, index) => Render(stones == 0 ? "view.heap.empty" : "view.heap", ("number", Number(index + 1)), ("stones", Stones(stones)), ("count", Number(stones)))));

    /// <summary>The heaps as a short list of numbers, for example <c>3, 4, 5</c>.</summary>
    public static string Sizes(IEnumerable<int> heaps) => string.Join(", ", heaps.Select(Number));

    /// <summary>A whole number as text.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private IEnumerable<string> RuleLines(NimOptions options)
    {
        yield return Render(options.Misere ? "rules.goal.misere" : "rules.goal.normal");
        if (options.MaxTake is { } max)
        {
            yield return Render("rules.max-take", ("max", Number(max)));
        }

        if (options.AllowResign)
        {
            yield return Render("rules.resign");
        }

        if (options.PassTemptation)
        {
            yield return Render("rules.pass");
            yield return Render("rules.move-limit", ("limit", Number(options.MaxMoves)));
        }
    }

    private IEnumerable<string> OptionLines(NimOptions options)
    {
        yield return Render(options.Misere ? "option.misere" : "option.normal");
        if (options.MaxTake is { } max)
        {
            yield return Render("option.max-take", ("max", Number(max)));
        }

        if (options.AllowResign)
        {
            yield return Render("option.resign");
        }
    }

    private static string Stones(int count) =>
        string.Join(' ', Enumerable.Range(0, (count + GroupSize - 1) / GroupSize).Select(group => new string(Stone, Math.Min(GroupSize, count - group * GroupSize))));

    private string Render(string key, params (string Name, string Value)[] values) => templates.Render(TemplateRef.Of(key, values), language);
}
