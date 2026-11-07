// Adapted from AiBehavior.PublicGoods (MIT, same author).
using System.Text;

namespace LlmShootout.Arena.Answers;

/// <summary>One invalid or missing field of a model answer.</summary>
/// <param name="Key">The JSON key.</param>
/// <param name="Violation">What is wrong, as a short sentence without trailing period.</param>
/// <param name="Allowed">The allowed values, or empty if there is no useful enumeration.</param>
/// <param name="Code">A stable machine-readable code of the rule that was broken (from <c>MoveCheck</c>), or null. It goes into the protocol, not into the hint.</param>
public sealed record FieldViolation(string Key, string Violation, string Allowed = "", string? Code = null);

/// <summary>The fixed phrases of a correction hint, so that hints can be worded in any language.</summary>
/// <param name="InvalidFieldsHeader">Introduces the list of invalid fields.</param>
/// <param name="AllowedLabel">Introduces the allowed values of a field.</param>
/// <param name="UnknownFieldsHeader">Introduces the list of unknown keys.</param>
/// <param name="Generic">Used when there is nothing more specific to say.</param>
public sealed record HintTexts(string InvalidFieldsHeader, string AllowedLabel, string UnknownFieldsHeader, string Generic);

/// <summary>
/// Builds the text of a correction hint: names each invalid key, its violation and its allowed values.
/// </summary>
public static class CorrectionHintBuilder
{
    /// <summary>Builds the hint for the given violations and unknown keys.</summary>
    public static string Build(IReadOnlyList<FieldViolation> violations, IReadOnlyList<string> extraKeys, HintTexts texts)
    {
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentNullException.ThrowIfNull(extraKeys);
        ArgumentNullException.ThrowIfNull(texts);

        var sb = new StringBuilder();
        if (violations.Count > 0)
        {
            sb.Append(texts.InvalidFieldsHeader);
            foreach (var field in violations)
            {
                sb.Append("\n- \"").Append(field.Key).Append("\": ").Append(field.Violation).Append('.');
                if (!string.IsNullOrEmpty(field.Allowed))
                {
                    sb.Append(' ').Append(texts.AllowedLabel).Append(' ').Append(field.Allowed).Append('.');
                }
            }
        }

        if (extraKeys.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(texts.UnknownFieldsHeader).Append(' ')
              .Append(string.Join(", ", extraKeys.Select(k => $"\"{k}\"")))
              .Append('.');
        }

        return sb.Length > 0
            ? sb.ToString()
            : texts.Generic;
    }
}
