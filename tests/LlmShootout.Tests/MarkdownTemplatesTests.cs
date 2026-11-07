using LlmShootout.Arena.Games;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests;

public sealed class MarkdownTemplatesTests
{
    private static readonly MarkdownTemplates Templates = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = "# Header is ignored\n\n## greeting\nHello {name}!\nSecond line.\n\n## json\nAnswer {\"move\": 1} for {who}\n",
        ["de"] = "## greeting\nHallo {name}!\n",
    });

    [Fact]
    public void Renders_placeholders_and_keeps_multiline_bodies()
    {
        var text = Templates.Render(TemplateRef.Of("greeting", ("name", "Ada")), "en");

        Assert.Equal("Hello Ada!\nSecond line.", text);
    }

    [Fact]
    public void Leaves_json_braces_alone()
    {
        Assert.Equal("Answer {\"move\": 1} for you", Templates.Render(TemplateRef.Of("json", ("who", "you")), "en"));
    }

    [Fact]
    public void Selects_the_language()
    {
        Assert.Equal("Hallo Ada!", Templates.Render(TemplateRef.Of("greeting", ("name", "Ada")), "de"));
    }

    [Fact]
    public void Fails_early_on_missing_value_key_or_language()
    {
        Assert.Throws<KeyNotFoundException>(() => Templates.Render(new TemplateRef("greeting"), "en"));
        Assert.Throws<KeyNotFoundException>(() => Templates.Render(new TemplateRef("nope"), "en"));
        Assert.Throws<KeyNotFoundException>(() => Templates.Render(new TemplateRef("greeting"), "fr"));
        Assert.Throws<KeyNotFoundException>(() => Templates.Hash("fr"));
    }

    [Fact]
    public void Hash_changes_with_the_text_and_is_stable()
    {
        var other = MarkdownTemplates.FromText(new Dictionary<string, string> { ["en"] = "## a\nb" });

        Assert.Equal(Templates.Hash("en"), Templates.Hash("en"));
        Assert.NotEqual(Templates.Hash("en"), other.Hash("en"));
        Assert.True(Templates.Contains("en", "json"));
        Assert.False(Templates.Contains("de", "json"));
    }
}
