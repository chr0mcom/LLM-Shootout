using System.Text.Json.Nodes;

namespace LlmShootout.Arena.Games;

/// <summary>Whether a field of the model answer is forbidden, optional or required in a turn.</summary>
public enum FieldRule
{
    /// <summary>The field must be null or absent.</summary>
    Forbidden,

    /// <summary>The field may be given.</summary>
    Optional,

    /// <summary>The field must be given.</summary>
    Required,
}

/// <summary>Describes what the <c>move</c> field of an answer looks like.</summary>
/// <param name="Description">Human readable description, shown in the prompt.</param>
/// <param name="AllowedValues">The complete list of allowed moves if it is short, otherwise null.</param>
/// <param name="SchemaFragment">Optional JSON schema for the move, merged into the response schema.</param>
public sealed record MoveFormat(
    TemplateRef Description,
    IReadOnlyList<string>? AllowedValues = null,
    JsonNode? SchemaFragment = null);

/// <summary>One thing a seat is asked to do now.</summary>
/// <param name="TurnId">Stable id inside the match, for example <c>talk-3</c> or <c>decide</c>.</param>
/// <param name="Kind">Kind of turn, for example <c>talk</c>, <c>decide</c>, <c>set</c>, <c>guess</c>. Reports group by it.</param>
/// <param name="Seat">The seat that has to answer.</param>
/// <param name="Message">Whether a public message is forbidden, optional or required.</param>
/// <param name="Move">Whether a move is forbidden, optional or required.</param>
/// <param name="Instruction">What the player is asked to do, as a template.</param>
/// <param name="Format">The move format, or null if no move is allowed.</param>
/// <param name="Thought">Whether a private thought is forbidden, optional or required.</param>
/// <param name="Estimate">Whether a self-assessment (a whole number from 0 to 100) is forbidden, optional or required.</param>
/// <param name="EstimateMeaning">What the estimate stands for, for example "your chance of winning in percent". Required unless <paramref name="Estimate"/> is forbidden.</param>
public sealed record TurnRequest(
    string TurnId,
    string Kind,
    Seat Seat,
    FieldRule Message,
    FieldRule Move,
    TemplateRef Instruction,
    MoveFormat? Format = null,
    FieldRule Thought = FieldRule.Optional,
    FieldRule Estimate = FieldRule.Forbidden,
    TemplateRef? EstimateMeaning = null);
