using LlmShootout.Arena.Models;

namespace LlmShootout.Tests.Models;

public class ReasoningJsonRecoveryTests
{
    [Fact]
    public void Takes_the_last_parseable_object()
    {
        const string reasoning = "First idea {\"move\":\"split\"} but then {\"thought\":\"x\",\"move\":\"steal\"} final.";

        Assert.True(ReasoningJsonRecovery.TryExtractJsonObject(reasoning, out var json));
        Assert.Equal("{\"thought\":\"x\",\"move\":\"steal\"}", json);
    }

    [Fact]
    public void Ignores_braces_inside_strings()
    {
        const string reasoning = "{\"thought\":\"a } b { c\",\"move\":\"split\"}";

        Assert.True(ReasoningJsonRecovery.TryExtractJsonObject(reasoning, out var json));
        Assert.Equal(reasoning, json);
    }

    [Fact]
    public void Handles_escaped_quotes_inside_strings()
    {
        const string reasoning = "{\"thought\":\"say \\\"hi\\\" }\",\"move\":null}";

        Assert.True(ReasoningJsonRecovery.TryExtractJsonObject(reasoning, out var json));
        Assert.Equal(reasoning, json);
    }

    [Fact]
    public void Skips_unparseable_candidates()
    {
        const string reasoning = "{\"ok\":1} then {broken: yes}";

        Assert.True(ReasoningJsonRecovery.TryExtractJsonObject(reasoning, out var json));
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
        Assert.False(ReasoningJsonRecovery.TryExtractJsonObject(reasoning, out var json));
        Assert.Equal(string.Empty, json);
    }
}
