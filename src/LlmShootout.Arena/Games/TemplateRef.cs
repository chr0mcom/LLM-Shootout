namespace LlmShootout.Arena.Games;

/// <summary>
/// Text that is not yet worded: a template key plus the values to fill in. Games return these instead of
/// finished strings, so the wording lives in versioned template files per language and tests can assert on values.
/// </summary>
/// <param name="Key">Section key in the template file of the game (or of the arena).</param>
/// <param name="Values">Values for the <c>{placeholders}</c> of the template.</param>
public sealed record TemplateRef(string Key, IReadOnlyDictionary<string, string>? Values = null)
{
    /// <summary>Creates a reference from key and name/value pairs.</summary>
    public static TemplateRef Of(string key, params (string Name, string Value)[] values) =>
        new(key, values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));
}
