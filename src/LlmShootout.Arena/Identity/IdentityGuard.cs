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

/// <summary>
/// Checks every public message for names of the author's own model or the other player's model and returns the hits
/// as a technical marker for the protocol. A name that belongs to both models (self-play, same provider) is marked
/// ambiguous. Messages are never changed.
/// </summary>
public sealed class IdentityGuard
{
    private readonly Dictionary<ModelIdentity, IdentityTokenSet> _tokens = [];
    private readonly Func<Seat, ModelIdentity?> _models;
    private readonly IdentityScannerSettings _settings;

    /// <summary>
    /// Creates the guard. <paramref name="models"/> is asked for every message, so a version that a provider reports
    /// only after the first call is picked up. Seats without a model (random, scripted without identity) have no names to find.
    /// </summary>
    public IdentityGuard(Func<Seat, ModelIdentity?> models, IdentityScannerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(settings);
        _models = models;
        _settings = settings;
    }

    /// <summary>Scans a message of the given author. Returns the hits as <c>subject:type:name</c>, for example <c>opponent:Provider:openai</c>.</summary>
    public IReadOnlyList<string> Inspect(Seat author, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return [];
        }

        var own = ScanFor(author, message);
        var other = ScanFor(author.Opponent, message);
        var ambiguous = own.Matches.Select(m => m.CanonicalToken).Intersect(other.Matches.Select(m => m.CanonicalToken), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hits = new List<string>();
        hits.AddRange(Describe(own, LeakedIdentitySubject.Self, ambiguous));
        hits.AddRange(Describe(other, LeakedIdentitySubject.Opponent, ambiguous));
        return [.. hits.Distinct(StringComparer.Ordinal)];
    }

    private IdentityScanResult ScanFor(Seat seat, string message)
    {
        if (_models(seat) is not { } model)
        {
            return new IdentityScanResult(false, [], false);
        }

        if (!_tokens.TryGetValue(model, out var set))
        {
            set = IdentityScanner.BuildTokenSet(model, _settings);
            _tokens[model] = set;
        }

        return IdentityScanner.Scan(message, set, _settings);
    }

    private static IEnumerable<string> Describe(IdentityScanResult result, LeakedIdentitySubject subject, HashSet<string> ambiguous) =>
        result.Matches.Select(m => $"{(ambiguous.Contains(m.CanonicalToken) ? LeakedIdentitySubject.Ambiguous : subject).ToString().ToLowerInvariant()}:{m.MarkerType}:{m.CanonicalToken}");
}
