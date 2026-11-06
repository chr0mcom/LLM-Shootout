// Adapted from AiBehavior.PublicGoods (MIT, same author).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LlmShootout.Arena.Identity;

/// <summary>
/// Finds names of models (slug, provider, aliases) in public messages. Matching ignores case, accents and
/// separators, so <c>GPT 4o</c> and <c>gpt-4o</c> both match, but it respects word boundaries, so
/// <c>google</c> does not match inside <c>googled</c>.
/// </summary>
public static class IdentityScanner
{
    /// <summary>Scans a message for the names in <paramref name="tokenSet"/>.</summary>
    public static IdentityScanResult Scan(string publicMessage, IdentityTokenSet tokenSet, IdentityScannerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(publicMessage);
        ArgumentNullException.ThrowIfNull(tokenSet);
        ArgumentNullException.ThrowIfNull(settings);

        var matches = FindMatches(NormalizedTextView.Create(publicMessage, false), tokenSet.Tokens);
        if (matches.Count > 0)
        {
            return BuildResult(publicMessage, settings.RedactionPlaceholder, matches, false);
        }

        if (!settings.EnableLeetspeak)
        {
            return new IdentityScanResult(false, publicMessage, [], false);
        }

        var leetTokens = tokenSet.Tokens
            .Select(token => token with { NormalizedToken = Normalize(token.CanonicalToken, true) })
            .Where(token => token.NormalizedToken.Length > 0)
            .ToArray();
        matches = FindMatches(NormalizedTextView.Create(publicMessage, true), leetTokens);
        return matches.Count > 0
            ? BuildResult(publicMessage, settings.RedactionPlaceholder, matches, true)
            : new IdentityScanResult(false, publicMessage, [], true);
    }

    /// <summary>Lower-cases, strips accents and removes everything except letters and digits.</summary>
    public static string Normalize(string value, bool enableLeetspeak = false)
        => NormalizedTextView.Create(value, enableLeetspeak).Normalized;

    /// <summary>Builds the searchable names of one model.</summary>
    public static IdentityTokenSet BuildTokenSet(ModelIdentity model, IdentityScannerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(settings);

        var provider = PromptIdentityFormatter.GetProvider(model.Slug);
        var withoutProvider = PromptIdentityFormatter.WithoutProviderPrefix(model.Slug);
        var seen = new Dictionary<string, IdentityToken>(StringComparer.Ordinal);

        AddToken(seen, model.Slug, IdentityLeakMarkerType.ExactName);
        if (!string.Equals(model.ResolvedVersion, model.Slug, StringComparison.OrdinalIgnoreCase))
        {
            AddToken(seen, model.ResolvedVersion, IdentityLeakMarkerType.ExactName);
        }

        AddToken(seen, withoutProvider, IdentityLeakMarkerType.NormalizedVariant);
        AddToken(seen, withoutProvider.Replace('-', ' '), IdentityLeakMarkerType.NormalizedVariant);
        AddToken(seen, withoutProvider.Replace('_', ' '), IdentityLeakMarkerType.NormalizedVariant);
        AddToken(seen, provider, IdentityLeakMarkerType.Provider);
        AddAliases(seen, settings.ProviderAliases, provider, IdentityLeakMarkerType.Provider);
        AddAliases(seen, settings.ModelAliases, model.Slug, IdentityLeakMarkerType.Alias);

        var tokens = seen.Values
            .OrderByDescending(x => x.NormalizedToken.Length)
            .ThenBy(x => x.CanonicalToken, StringComparer.Ordinal)
            .ToArray();

        var fingerprintSource = string.Join('|', tokens.Select(x => $"{x.MarkerType}:{x.NormalizedToken}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)));
        return new IdentityTokenSet(PromptIdentityFormatter.Format(model), tokens, fingerprint);
    }

    private static void AddAliases(
        Dictionary<string, IdentityToken> seen,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases,
        string key,
        IdentityLeakMarkerType markerType)
    {
        if (aliases is not null && aliases.TryGetValue(key, out var list))
        {
            foreach (var alias in list)
            {
                AddToken(seen, alias, markerType);
            }
        }
    }

    private static void AddToken(Dictionary<string, IdentityToken> seen, string value, IdentityLeakMarkerType markerType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = Normalize(value);
        if (normalized.Length > 0)
        {
            seen.TryAdd(normalized, new IdentityToken(value.Trim(), normalized, markerType));
        }
    }

    private static List<MatchSpan> FindMatches(NormalizedTextView view, IReadOnlyList<IdentityToken> tokens)
    {
        var matches = new List<MatchSpan>();
        foreach (var token in tokens.Where(t => t.NormalizedToken.Length > 0))
        {
            var startIndex = 0;
            while (startIndex < view.Normalized.Length)
            {
                var hit = view.Normalized.IndexOf(token.NormalizedToken, startIndex, StringComparison.Ordinal);
                if (hit < 0)
                {
                    break;
                }

                var originalStart = view.Map[hit];
                var originalEnd = view.Map[hit + token.NormalizedToken.Length - 1];
                if (IsBoundary(view.Original, originalStart - 1) && IsBoundary(view.Original, originalEnd + 1))
                {
                    matches.Add(new MatchSpan(
                        originalStart,
                        originalEnd,
                        new IdentityLeakMatch(token.MarkerType, token.CanonicalToken, token.CanonicalToken)));
                    startIndex = hit + token.NormalizedToken.Length;
                }
                else
                {
                    startIndex = hit + 1;
                }
            }
        }

        return [.. matches.OrderBy(x => x.Start).ThenByDescending(x => x.End)];
    }

    private static bool IsBoundary(string original, int index)
        => index < 0 || index >= original.Length || !char.IsLetterOrDigit(original[index]);

    private static IdentityScanResult BuildResult(
        string original,
        string placeholder,
        List<MatchSpan> matches,
        bool leetspeakApplied)
    {
        var merged = Merge(matches);
        var builder = new StringBuilder();
        var cursor = 0;
        foreach (var span in merged)
        {
            if (span.Start > cursor)
            {
                builder.Append(original, cursor, span.Start - cursor);
            }

            builder.Append(placeholder);
            cursor = span.End + 1;
        }

        if (cursor < original.Length)
        {
            builder.Append(original, cursor, original.Length - cursor);
        }

        return new IdentityScanResult(true, builder.ToString(), [.. merged.Select(x => x.Match).Distinct()], leetspeakApplied);
    }

    private static List<MatchSpan> Merge(List<MatchSpan> spans)
    {
        var merged = new List<MatchSpan> { spans[0] };
        foreach (var span in spans.Skip(1))
        {
            var current = merged[^1];
            if (span.Start <= current.End)
            {
                merged[^1] = current with { End = Math.Max(current.End, span.End) };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    private sealed record NormalizedTextView(string Original, string Normalized, int[] Map)
    {
        public static NormalizedTextView Create(string value, bool enableLeetspeak)
        {
            var normalized = new StringBuilder();
            var map = new List<int>(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (char.IsSurrogate(current))
                {
                    normalized.Append(current);
                    map.Add(index);
                    continue;
                }

                foreach (var normalizedChar in NormalizeChar(current, enableLeetspeak))
                {
                    normalized.Append(normalizedChar);
                    map.Add(index);
                }
            }

            return new NormalizedTextView(value, normalized.ToString(), [.. map]);
        }

        private static IEnumerable<char> NormalizeChar(char value, bool enableLeetspeak)
        {
            foreach (var ch in value.ToString().Normalize(NormalizationForm.FormKD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                var lowered = char.ToLowerInvariant(ch);
                if (enableLeetspeak)
                {
                    lowered = lowered switch
                    {
                        '0' => 'o',
                        '1' => 'i',
                        '3' => 'e',
                        '4' => 'a',
                        '5' => 's',
                        '7' => 't',
                        _ => lowered,
                    };
                }

                if (char.IsLetterOrDigit(lowered))
                {
                    yield return lowered;
                }
            }
        }
    }

    private sealed record MatchSpan(int Start, int End, IdentityLeakMatch Match);
}
