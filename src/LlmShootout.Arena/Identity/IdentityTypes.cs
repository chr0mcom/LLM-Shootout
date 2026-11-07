// Adapted from AiBehavior.PublicGoods (MIT, same author).
namespace LlmShootout.Arena.Identity;

/// <summary>Identifies a model: the requested slug and, if known, the version the provider reported.</summary>
/// <param name="Slug">The model name as given in the player specification, for example <c>openai/gpt-4o</c> (OpenRouter) or <c>claude-haiku-latest</c> (another endpoint).</param>
/// <param name="Version">The model version reported in the response, or null if unknown.</param>
public sealed record ModelIdentity(string Slug, string? Version = null)
{
    /// <summary>The reported version, or the slug if no version is known.</summary>
    public string ResolvedVersion => string.IsNullOrWhiteSpace(Version) ? Slug : Version;
}

/// <summary>Settings of the identity scanner.</summary>
/// <param name="EnableLeetspeak">Also match names written with digits for letters, such as <c>cl4ude-s0nnet</c>.</param>
/// <param name="RedactionPlaceholder">Text that replaces a match when a message is redacted.</param>
/// <param name="ModelAliases">Additional names per model slug.</param>
/// <param name="ProviderAliases">Additional names per provider prefix.</param>
public sealed record IdentityScannerSettings(
    bool EnableLeetspeak = false,
    string RedactionPlaceholder = "[IDENTITY REDACTED]",
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ModelAliases = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ProviderAliases = null);

/// <summary>One searchable name of a model.</summary>
public sealed record IdentityToken(string CanonicalToken, string NormalizedToken, IdentityLeakMarkerType MarkerType);

/// <summary>All searchable names of one model plus a fingerprint for the protocol.</summary>
public sealed record IdentityTokenSet(string Identity, IReadOnlyList<IdentityToken> Tokens, string Fingerprint);

/// <summary>A single hit in a message.</summary>
public sealed record IdentityLeakMatch(IdentityLeakMarkerType MarkerType, string MatchedToken, string CanonicalToken);

/// <summary>Result of scanning one message.</summary>
/// <param name="HasLeak">True if at least one name was found.</param>
/// <param name="RedactedMessage">The message with all hits replaced by the placeholder.</param>
/// <param name="Matches">The distinct hits.</param>
/// <param name="LeetspeakApplied">True if the hit needed leetspeak normalization.</param>
public sealed record IdentityScanResult(
    bool HasLeak,
    string RedactedMessage,
    IReadOnlyList<IdentityLeakMatch> Matches,
    bool LeetspeakApplied);

/// <summary>What kind of name was found.</summary>
public enum IdentityLeakMarkerType
{
    /// <summary>The full slug or version.</summary>
    ExactName,

    /// <summary>The provider prefix, for example <c>openai</c>.</summary>
    Provider,

    /// <summary>A configured alias.</summary>
    Alias,

    /// <summary>The slug without provider prefix, with or without separators.</summary>
    NormalizedVariant,
}

/// <summary>Whose identity a message mentions.</summary>
public enum LeakedIdentitySubject
{
    /// <summary>The author of the message named itself.</summary>
    Self,

    /// <summary>The author named the opponent.</summary>
    Opponent,

    /// <summary>The hit belongs to both identities, for example in self-play or with a shared provider.</summary>
    Ambiguous,
}
