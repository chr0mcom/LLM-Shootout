// Adapted from AiBehavior.PublicGoods (MIT, same author).
using System.Text;

namespace LlmShootout.Arena.Debriefing;

/// <summary>One invalid or missing field of a model answer.</summary>
/// <param name="Key">The JSON key.</param>
/// <param name="Violation">What is wrong, as a short sentence without trailing period.</param>
/// <param name="Allowed">The allowed values, or empty if there is no useful enumeration.</param>
public sealed record FieldViolation(string Key, string Violation, string Allowed = "");

/// <summary>
/// Builds the text of a correction hint: names each invalid key, its violation and its allowed values.
/// </summary>
public static class CorrectionHintBuilder
{
    /// <summary>Builds the hint for the given violations and unknown keys.</summary>
    public static string Build(IReadOnlyList<FieldViolation> violations, IReadOnlyList<string> extraKeys)
    {
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentNullException.ThrowIfNull(extraKeys);

        var sb = new StringBuilder();
        if (violations.Count > 0)
        {
            sb.Append("Invalid or missing fields:");
            foreach (var (key, violation, allowed) in violations)
            {
                sb.Append("\n- \"").Append(key).Append("\": ").Append(violation).Append('.');
                if (!string.IsNullOrEmpty(allowed))
                {
                    sb.Append(" Allowed: ").Append(allowed).Append('.');
                }
            }
        }

        if (extraKeys.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append("Remove unknown field(s): ")
              .Append(string.Join(", ", extraKeys.Select(k => $"\"{k}\"")))
              .Append('.');
        }

        return sb.Length > 0
            ? sb.ToString()
            : "Your previous response was not a valid JSON object matching the required format.";
    }
}
