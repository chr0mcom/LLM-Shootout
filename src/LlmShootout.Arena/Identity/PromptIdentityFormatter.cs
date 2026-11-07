// Adapted from AiBehavior.PublicGoods (MIT, same author).
namespace LlmShootout.Arena.Identity;

/// <summary>Helpers that split and format model slugs (<c>provider/model</c> on OpenRouter, a bare model name on other endpoints).</summary>
public static class PromptIdentityFormatter
{
    /// <summary>Formats an identity as <c>slug (version)</c>, or just the slug if both are equal.</summary>
    public static string Format(ModelIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return string.Equals(identity.Slug, identity.ResolvedVersion, StringComparison.OrdinalIgnoreCase)
            ? identity.Slug
            : $"{identity.Slug} ({identity.ResolvedVersion})";
    }

    /// <summary>Returns the provider prefix of a slug such as <c>openai/gpt-4o</c>, or the slug itself.</summary>
    public static string GetProvider(string modelSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);
        var separator = modelSlug.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 ? modelSlug[..separator] : modelSlug;
    }

    /// <summary>Removes an OpenRouter variant suffix such as <c>:free</c> or <c>:thinking</c>.</summary>
    public static string WithoutVariant(string modelSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);
        var colon = modelSlug.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? modelSlug[..colon] : modelSlug;
    }

    /// <summary>Returns the part after the provider prefix, or the slug itself.</summary>
    public static string WithoutProviderPrefix(string modelSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);
        var separator = modelSlug.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 && separator + 1 < modelSlug.Length ? modelSlug[(separator + 1)..] : modelSlug;
    }
}
