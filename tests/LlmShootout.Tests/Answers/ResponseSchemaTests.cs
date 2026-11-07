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
}
