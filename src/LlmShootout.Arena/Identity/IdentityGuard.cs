using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Identity;

/// <summary>Well-known names of model families, so that "I am Claude" is recognized as a hit for an Anthropic model.</summary>
public static class IdentityAliases
{
    /// <summary>Alias names per OpenRouter provider prefix.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Providers { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["openai"] = ["ChatGPT", "GPT"],
        ["anthropic"] = ["Claude"],
        ["google"] = ["Gemini", "Bard"],
        ["meta-llama"] = ["Llama"],
        ["mistralai"] = ["Mistral"],
        ["deepseek"] = ["DeepSeek"],
        ["x-ai"] = ["Grok"],
        ["qwen"] = ["Qwen"],
        ["moonshotai"] = ["Kimi"],
    };
}

/// <summary>A public message after the identity check.</summary>
/// <param name="Text">The message to publish: unchanged, or redacted if redaction is on. Null for silence.</param>
/// <param name="Hits">Hits as <c>subject:type:name</c>, for example <c>opponent:Provider:openai</c>.</param>
public sealed record InspectedMessage(string? Text, IReadOnlyList<string> Hits);

/// <summary>
/// Checks every public message for names of the author's own model or the opponent's model. A name that belongs to
/// both models (self-play, same provider) is marked ambiguous. By default messages are only marked, not changed.
/// </summary>
public sealed class IdentityGuard
{
    private readonly Dictionary<Seat, IdentityTokenSet> _tokens = [];
    private readonly IdentityScannerSettings _settings;
    private readonly bool _redact;

    /// <summary>Creates the guard. Seats without a model (random, scripted without identity) have no names to find.</summary>
    public IdentityGuard(IReadOnlyDictionary<Seat, ModelIdentity?> models, IdentityScannerSettings settings, bool redact)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _redact = redact;
        foreach (var (seat, model) in models)
        {
            if (model is not null)
            {
                _tokens[seat] = IdentityScanner.BuildTokenSet(model, settings);
            }
        }
    }

    /// <summary>Scans a message of the given author.</summary>
    public InspectedMessage Inspect(Seat author, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return new InspectedMessage(message, []);
        }

        var own = ScanFor(author, message);
        var other = ScanFor(author.Opponent, message);
        var ambiguous = own.Matches.Select(m => m.CanonicalToken).Intersect(other.Matches.Select(m => m.CanonicalToken), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hits = new List<string>();
        hits.AddRange(Describe(own, LeakedIdentitySubject.Self, ambiguous));
        hits.AddRange(Describe(other, LeakedIdentitySubject.Opponent, ambiguous));
        var distinct = hits.Distinct(StringComparer.Ordinal).ToArray();

        var text = _redact && distinct.Length > 0 ? ScanFor(author.Opponent, own.RedactedMessage).RedactedMessage : message;
        return new InspectedMessage(text, distinct);
    }

    private IdentityScanResult ScanFor(Seat seat, string message) =>
        _tokens.TryGetValue(seat, out var set)
            ? IdentityScanner.Scan(message, set, _settings)
            : new IdentityScanResult(false, message, [], false);

    private static IEnumerable<string> Describe(IdentityScanResult result, LeakedIdentitySubject subject, HashSet<string> ambiguous) =>
        result.Matches.Select(m => $"{(ambiguous.Contains(m.CanonicalToken) ? LeakedIdentitySubject.Ambiguous : subject).ToString().ToLowerInvariant()}:{m.MarkerType}:{m.CanonicalToken}");
}
