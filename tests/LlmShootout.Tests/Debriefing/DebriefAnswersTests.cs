namespace LlmShootout.Tests.Debriefing;

public sealed class DebriefAnswersTests
{
    private static readonly LlmShootout.Arena.Debriefing.DebriefingQuestion[] Questions =
    [
        new("scale", LlmShootout.Arena.Debriefing.QuestionKind.Scale, new LlmShootout.Arena.Games.TemplateRef("x"), Min: 1, Max: 5),
        new("flag", LlmShootout.Arena.Debriefing.QuestionKind.Boolean, new LlmShootout.Arena.Games.TemplateRef("x"), WithReason: true),
        new("pick", LlmShootout.Arena.Debriefing.QuestionKind.Choice, new LlmShootout.Arena.Games.TemplateRef("x"), Choices: ["red", "blue"]),
        new("free", LlmShootout.Arena.Debriefing.QuestionKind.Text, new LlmShootout.Arena.Games.TemplateRef("x")),
    ];

    private static (Dictionary<string, string> Answers, List<(string Key, LlmShootout.Arena.Debriefing.QuestionKind Kind, string Problem)> Violations) Check(string json) =>
        LlmShootout.Arena.Debriefing.DebriefAnswers.Validate(Questions, System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());

    [Fact]
    public void A_valid_answer_passes_with_case_insensitive_choices()
    {
        var (answers, violations) = Check("{\"scale\":3,\"flag\":true,\"flag_reason\":\"because\",\"pick\":\"RED\",\"free\":\"text\"}");

        Assert.Empty(violations);
        Assert.Equal("red", answers["pick"]);
        Assert.Equal("true", answers["flag"]);
    }

    [Theory]
    [InlineData("{\"scale\":\"3\",\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":6,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":3.5,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    [InlineData("{\"scale\":3,\"flag\":\"ja\",\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "flag")]
    [InlineData("{\"scale\":3,\"flag\":true,\"pick\":\"red\",\"free\":\"t\"}", "flag_reason")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"green\",\"free\":\"t\"}", "pick")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"\"}", "free")]
    [InlineData("{\"scale\":3,\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":42}", "free")]
    [InlineData("{\"flag\":true,\"flag_reason\":\"r\",\"pick\":\"red\",\"free\":\"t\"}", "scale")]
    public void Wrong_types_values_and_missing_keys_are_violations(string json, string key)
    {
        var (_, violations) = Check(json);

        Assert.Equal(key, Assert.Single(violations).Key);
    }
}
