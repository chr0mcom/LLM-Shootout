using LlmShootout.Arena.Identity;

namespace LlmShootout.Tests.Identity;

public class IdentityScannerTests
{
    private static readonly IdentityScannerSettings Defaults = new();

    private static IdentityScanResult Scan(string message, string slug, IdentityScannerSettings? settings = null)
    {
        var tokens = IdentityScanner.BuildTokenSet(new ModelIdentity(slug), settings ?? Defaults);
        return IdentityScanner.Scan(message, tokens, settings ?? Defaults);
    }

    [Theory]
    [InlineData("I think you are openai/gpt-4o.")]
    [InlineData("You sound like GPT 4o to me.")]
    [InlineData("Are you OpenAI?")]
    [InlineData("gpt_4o is my guess")]
    public void Finds_slug_variants_and_provider(string message)
    {
        var result = Scan(message, "openai/gpt-4o");

        Assert.True(result.HasLeak);
        Assert.Contains("[IDENTITY REDACTED]", result.RedactedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Respects_word_boundaries()
    {
        Assert.False(Scan("I googled it yesterday.", "google/gemini-pro").HasLeak);
    }

    [Fact]
    public void Leaves_a_clean_message_untouched()
    {
        var result = Scan("Let us split, I promise.", "anthropic/claude-sonnet");

        Assert.False(result.HasLeak);
        Assert.Equal("Let us split, I promise.", result.RedactedMessage);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Ignores_accents_and_case()
    {
        Assert.True(Scan("MÍSTRÁL here", "mistralai/mistral-large", new IdentityScannerSettings(ProviderAliases: new Dictionary<string, IReadOnlyList<string>> { ["mistralai"] = ["Mistral"] })).HasLeak);
    }

    [Fact]
    public void Finds_leetspeak_only_when_enabled()
    {
        var off = Scan("maybe you are cl4ude-s0nnet", "anthropic/claude-sonnet");
        var on = Scan("maybe you are cl4ude-s0nnet", "anthropic/claude-sonnet", new IdentityScannerSettings(EnableLeetspeak: true));

        Assert.False(off.HasLeak);
        Assert.True(on.HasLeak);
        Assert.True(on.LeetspeakApplied);
    }

    [Fact]
    public void Merges_overlapping_hits_into_one_redaction()
    {
        var result = Scan("openai/gpt-4o", "openai/gpt-4o");

        Assert.Equal("[IDENTITY REDACTED]", result.RedactedMessage);
    }

    [Fact]
    public void Adds_model_and_provider_aliases()
    {
        var settings = new IdentityScannerSettings(
            ModelAliases: new Dictionary<string, IReadOnlyList<string>> { ["openai/gpt-4o"] = ["Omni"] });

        Assert.True(Scan("are you omni?", "openai/gpt-4o", settings).HasLeak);
    }

    [Fact]
    public void Token_set_has_a_stable_fingerprint_and_display_name()
    {
        var a = IdentityScanner.BuildTokenSet(new ModelIdentity("openai/gpt-4o", "gpt-4o-2024-08-06"), Defaults);
        var b = IdentityScanner.BuildTokenSet(new ModelIdentity("openai/gpt-4o", "gpt-4o-2024-08-06"), Defaults);

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal("openai/gpt-4o (gpt-4o-2024-08-06)", a.Identity);
    }

    [Theory]
    [InlineData("openai/gpt-4o", "openai")]
    [InlineData("plain", "plain")]
    public void Splits_provider(string slug, string provider)
    {
        Assert.Equal(provider, PromptIdentityFormatter.GetProvider(slug));
    }

    [Theory]
    [InlineData("openai/gpt-4o", "gpt-4o")]
    [InlineData("plain", "plain")]
    [InlineData("trailing/", "trailing/")]
    public void Strips_provider_prefix(string slug, string expected)
    {
        Assert.Equal(expected, PromptIdentityFormatter.WithoutProviderPrefix(slug));
    }

    [Fact]
    public void Formats_identity_without_duplicate_version()
    {
        Assert.Equal("a/b", PromptIdentityFormatter.Format(new ModelIdentity("a/b", "A/B")));
    }
}
