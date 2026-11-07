using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;

namespace LlmShootout.Tests.Answers;

public sealed class ResponseSchemaTests
{
    [Fact]
    public void Response_schema_lists_allowed_moves()
    {
        var turn = new TurnRequest("t", "k", Seat.A, FieldRule.Forbidden, FieldRule.Required, new TemplateRef("i"), new MoveFormat(new TemplateRef("f"), ["split", "steal"]));

        var schema = ResponseSchema.ForTurn(turn).ToJsonString();

        Assert.Contains("\"enum\":[\"split\",\"steal\",null]", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"thought\",\"message\",\"move\"]", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_schema_uses_a_game_fragment_when_given()
    {
        var fragment = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"integer\"}");
        var turn = new TurnRequest("t", "k", Seat.A, FieldRule.Forbidden, FieldRule.Required, new TemplateRef("i"), new MoveFormat(new TemplateRef("f"), null, fragment));

        Assert.Contains("\"move\":{\"type\":\"integer\"}", ResponseSchema.ForTurn(turn).ToJsonString(), StringComparison.Ordinal);
    }

    private static TurnRequest Turn(FieldRule estimate) =>
        new("t", "k", Seat.A, FieldRule.Forbidden, FieldRule.Required, new TemplateRef("i"), new MoveFormat(new TemplateRef("f"), ["split", "steal"]), Estimate: estimate, EstimateMeaning: estimate == FieldRule.Forbidden ? null : new TemplateRef("m"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_an_estimate_the_schema_has_no_estimate_key_at_all(bool withMessages)
    {
        var schema = ResponseSchema.ForTurn(Turn(FieldRule.Forbidden), withMessages);

        Assert.DoesNotContain("estimate", schema.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_estimate_is_a_required_key_with_an_integer_or_null_from_0_to_100()
    {
        var schema = ResponseSchema.ForTurn(Turn(FieldRule.Optional));

        Assert.Equal("{\"type\":[\"integer\",\"null\"],\"minimum\":0,\"maximum\":100}", schema["properties"]!["estimate"]!.ToJsonString());
        Assert.Equal(["thought", "message", "move", "estimate"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void A_required_estimate_does_not_allow_null()
    {
        var schema = ResponseSchema.ForTurn(Turn(FieldRule.Required), false);

        Assert.Equal("{\"type\":\"integer\",\"minimum\":0,\"maximum\":100}", schema["properties"]!["estimate"]!.ToJsonString());
        Assert.Equal(["thought", "move", "estimate"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["thought", "move", "estimate"], ((JsonObject)schema["properties"]!).Select(p => p.Key));
    }
}
