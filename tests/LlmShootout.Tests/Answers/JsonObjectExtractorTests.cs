using LlmShootout.Arena.Answers;

namespace LlmShootout.Tests.Answers;

public sealed class JsonObjectExtractorTests
{
    [Fact]
    public void Takes_the_last_parseable_object()
    {
        const string reasoning = "First idea {\"move\":\"split\"} but then {\"thought\":\"x\",\"move\":\"steal\"} final.";

        Assert.True(JsonObjectExtractor.TryExtract(reasoning, out var json));
        Assert.Equal("{\"thought\":\"x\",\"move\":\"steal\"}", json);
    }

    [Fact]
    public void Ignores_braces_inside_strings()
    {
        const string reasoning = "{\"thought\":\"a } b { c\",\"move\":\"split\"}";

        Assert.True(JsonObjectExtractor.TryExtract(reasoning, out var json));
        Assert.Equal(reasoning, json);
    }

    [Fact]
    public void Handles_escaped_quotes_inside_strings()
    {
        const string reasoning = "{\"thought\":\"say \\\"hi\\\" }\",\"move\":null}";

        Assert.True(JsonObjectExtractor.TryExtract(reasoning, out var json));
        Assert.Equal(reasoning, json);
    }

    [Fact]
    public void Skips_unparseable_candidates()
    {
        const string reasoning = "{\"ok\":1} then {broken: yes}";

        Assert.True(JsonObjectExtractor.TryExtract(reasoning, out var json));
        Assert.Equal("{\"ok\":1}", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no json at all")]
    [InlineData("} stray {")]
    public void Returns_false_without_an_object(string? reasoning)
    {
        Assert.False(JsonObjectExtractor.TryExtract(reasoning, out var json));
        Assert.Equal(string.Empty, json);
    }

    [Fact]
    public void An_unbalanced_quote_in_the_prose_before_the_object_does_not_hide_it()
    {
        const string text = "He wrote \"my answer follows. Answer: {\"move\":\"split\"}";

        Assert.True(JsonObjectExtractor.TryExtract(text, out var json));
        Assert.Equal("{\"move\":\"split\"}", json);
    }

    [Fact]
    public void The_content_wins_over_the_reasoning_and_the_reasoning_is_the_fallback()
    {
        var both = JsonObjectExtractor.FindAnswer("{\"move\":\"split\"}", "{\"move\":\"steal\"}");
        var onlyReasoning = JsonObjectExtractor.FindAnswer("no json here", "so... {\"move\":\"steal\"}");

        Assert.Equal(("split", false), (both!.Value.Answer["move"]!.ToString(), both.Value.FromReasoning));
        Assert.Equal(("steal", true), (onlyReasoning!.Value.Answer["move"]!.ToString(), onlyReasoning.Value.FromReasoning));
        Assert.Null(JsonObjectExtractor.FindAnswer("nothing", null));
    }
}
