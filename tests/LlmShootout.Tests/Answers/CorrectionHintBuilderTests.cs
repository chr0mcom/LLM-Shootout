using LlmShootout.Arena.Answers;

namespace LlmShootout.Tests.Answers;

public sealed class CorrectionHintBuilderTests
{
    private static readonly HintTexts English = new(
        "Invalid or missing fields:",
        "Allowed:",
        "Remove unknown field(s):",
        "Your previous response was not a valid JSON object matching the required format.");

    [Fact]
    public void Names_each_violation_with_allowed_values()
    {
        var hint = CorrectionHintBuilder.Build(
            [new FieldViolation("move", "must be one of the allowed values", "\"split\", \"steal\""), new FieldViolation("thought", "is missing")],
            [],
            English);

        Assert.Equal(
            "Invalid or missing fields:\n- \"move\": must be one of the allowed values. Allowed: \"split\", \"steal\".\n- \"thought\": is missing.",
            hint);
    }

    [Fact]
    public void Lists_unknown_keys()
    {
        var hint = CorrectionHintBuilder.Build([new FieldViolation("move", "is missing")], ["foo", "bar"], English);

        Assert.EndsWith("\nRemove unknown field(s): \"foo\", \"bar\".", hint);
    }

    [Fact]
    public void Falls_back_to_a_generic_hint()
    {
        Assert.StartsWith("Your previous response was not a valid JSON object", CorrectionHintBuilder.Build([], [], English));
    }
}
