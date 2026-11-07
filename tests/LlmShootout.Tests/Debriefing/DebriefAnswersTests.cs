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

    [Fact]
    public void The_sample_answer_is_deterministic_neutral_and_valid()
    {
        var first = LlmShootout.Arena.Debriefing.DebriefAnswers.Sample(Questions, "No statement.");
        var second = LlmShootout.Arena.Debriefing.DebriefAnswers.Sample(Questions, "No statement.");

        Assert.Equal(first, second);
        var (answers, violations) = Check(first);
        Assert.Empty(violations);
        Assert.Equal(("3", "false", "red", "No statement."), (answers["scale"], answers["flag"], answers["pick"], answers["free"]));
        Assert.Equal("No statement.", answers["flag_reason"]);
    }

    [Fact]
    public void A_question_can_name_the_sample_answer_of_players_without_a_model()
    {
        LlmShootout.Arena.Debriefing.DebriefingQuestion[] questions =
        [
            new("rules_followed", LlmShootout.Arena.Debriefing.QuestionKind.Boolean, new LlmShootout.Arena.Games.TemplateRef("x"), WithReason: true, SampleAnswer: "true"),
        ];

        var json = System.Text.Json.Nodes.JsonNode.Parse(LlmShootout.Arena.Debriefing.DebriefAnswers.Sample(questions, "No statement."))!.AsObject();

        Assert.True(json["rules_followed"]!.GetValue<bool>());
    }

    [Fact]
    public void The_standard_questions_ask_about_play_and_rules_and_not_about_the_opponent_as_a_person()
    {
        var standard = LlmShootout.Arena.Debriefing.StandardQuestions.For([]);

        Assert.Equal(["strategy", "decisive_move", "own_mistake", "opponent_strength", "rules_followed", "next_time"], standard.Select(q => q.Key));
        var strength = standard.Single(q => q.Key == "opponent_strength");
        Assert.Equal((1, 5), (strength.Min, strength.Max));
        Assert.True(standard.Single(q => q.Key == "rules_followed").WithReason);
        var sample = System.Text.Json.Nodes.JsonNode.Parse(LlmShootout.Arena.Debriefing.DebriefAnswers.Sample(standard, "No statement."))!.AsObject();
        Assert.Empty(LlmShootout.Arena.Debriefing.DebriefAnswers.Validate(standard, sample).Violations);
    }
}
