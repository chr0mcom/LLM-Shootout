using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;

namespace LlmShootout.Arena.Prompting;

/// <summary>Everything a prompt for one turn is made of.</summary>
/// <param name="Seat">The seat that must answer.</param>
/// <param name="Game">The running game, for rules and view.</param>
/// <param name="Turn">The turn to answer.</param>
/// <param name="Timeline">The shared history.</param>
/// <param name="Thoughts">The private thoughts so far.</param>
/// <param name="PreviousAnswer">On a retry: the raw invalid answer.</param>
/// <param name="CorrectionHint">On a retry: what was wrong.</param>
/// <param name="AfterRefusal">True if the previous answer was a refusal.</param>
public sealed record TurnPromptInput(
    Seat Seat,
    IGame Game,
    TurnRequest Turn,
    Timeline Timeline,
    ThoughtLog Thoughts,
    string? PreviousAnswer = null,
    string? CorrectionHint = null,
    bool AfterRefusal = false);

/// <summary>
/// Builds the prompt of a turn from scratch: system message (contract and rules), one user message (view,
/// history, own notes, task), and on a retry the previous answer plus a correction hint.
/// </summary>
public sealed class PromptBuilder(ITemplateSource arenaTemplates, ITemplateSource gameTemplates, string language, MatchOptions options, bool communication)
{
    /// <summary>Renders a template of the arena.</summary>
    public string Arena(string key, params (string Name, string Value)[] values) =>
        arenaTemplates.Render(TemplateRef.Of(key, values), language);

    /// <summary>Renders a reference with the templates of the arena.</summary>
    public string RenderArena(TemplateRef reference) => arenaTemplates.Render(reference, language);

    /// <summary>Renders a reference with the templates of the game.</summary>
    public string RenderGame(TemplateRef reference) => gameTemplates.Render(reference, language);

    /// <summary>Builds the messages for one model call.</summary>
    public IReadOnlyList<PromptMessage> BuildTurn(TurnPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<PromptMessage> messages =
        [
            new(PromptRole.System, BuildSystem(input)),
            new(PromptRole.User, BuildUser(input)),
        ];

        if (input.PreviousAnswer is not null && input.CorrectionHint is not null)
        {
            messages.Add(new PromptMessage(PromptRole.Assistant, input.PreviousAnswer));
            var key = input.AfterRefusal ? "retry.refusal" : "retry.invalid";
            messages.Add(new PromptMessage(PromptRole.User, Arena(key, ("hint", input.CorrectionHint))));
        }

        return messages;
    }

    /// <summary>Renders one timeline entry for the viewing seat, or null if it has no text.</summary>
    public string? RenderEntry(TimelineEntry entry, Seat viewer) => entry switch
    {
        MessageEntry m => Arena("history.message", ("speaker", Speaker(m.Seat, viewer)), ("text", m.Text ?? Arena("history.silence"))),
        FactEntry f => RenderFact(f.Fact, viewer),
        _ => null,
    };

    /// <summary>
    /// Renders a fact for a viewing seat, or neutrally if <paramref name="viewer"/> is null (protocol, reports).
    /// The game template may use <c>{who}</c> and <c>{seat}</c>.
    /// </summary>
    public string RenderFact(GameFact fact, Seat? viewer)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var values = new Dictionary<string, string>(fact.Description.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (fact.Seat is { } seat)
        {
            values.TryAdd("who", viewer is { } v ? Speaker(seat, v) : Arena("who.seat", ("seat", seat.ToString())));
            values.TryAdd("seat", seat.ToString());
        }

        return RenderGame(fact.Description with { Values = values });
    }

    private string BuildSystem(TurnPromptInput input) =>
        Arena("system", ("rules", RenderGame(input.Game.RulesFor(input.Seat))));

    private string BuildUser(TurnPromptInput input)
    {
        var sb = new StringBuilder();
        AppendSection(sb, "section.view", RenderGame(input.Game.ViewFor(input.Seat)));

        var history = input.Timeline.VisibleTo(input.Seat).Select(e => RenderEntry(e, input.Seat)).OfType<string>().ToArray();
        AppendSection(sb, "section.history", history.Length == 0 ? Arena("history.empty") : string.Join('\n', history));

        var notes = input.Thoughts.Recent(input.Seat, options.ThoughtMemory);
        if (notes.Count > 0)
        {
            AppendSection(sb, "section.notes", string.Join('\n', notes.Select(n => "- " + n)));
        }

        AppendSection(sb, "section.task", RenderGame(input.Turn.Instruction) + "\n\n" + BuildContract(input.Turn));
        return sb.ToString().TrimEnd();
    }

    private string BuildContract(TurnRequest turn)
    {
        List<(string Key, string Line)> fields = [("thought", Field("thought", turn.Thought))];
        if (communication)
        {
            fields.Add(("message", Field("message", turn.Message)));
        }

        fields.Add(("move", Field("move", turn.Move, turn.Format is null ? null : RenderGame(turn.Format.Description))));
        if (turn.Estimate != FieldRule.Forbidden)
        {
            fields.Add(("estimate", EstimateField(turn)));
        }

        return Arena("contract", ("shape", Shape(fields.Select(f => f.Key))), ("fields", string.Join('\n', fields.Select(f => f.Line))));
    }

    private static string Shape(IEnumerable<string> keys) => "{" + string.Join(", ", keys.Select(key => $"\"{key}\": ...")) + "}";

    private string EstimateField(TurnRequest turn) => Arena(
        $"field.estimate.{turn.Estimate.ToString().ToLowerInvariant()}",
        ("meaning", RenderGame(turn.EstimateMeaning ?? throw new InvalidOperationException($"Turn '{turn.TurnId}' asks for an estimate but names no meaning for it."))));

    private string Field(string name, FieldRule rule, string? format = null) => Arena(
        $"field.{name}.{rule.ToString().ToLowerInvariant()}",
        ("max_thought_words", options.MaxThoughtWords.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("max_message_words", options.MaxMessageWords.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("format", format ?? string.Empty));

    private string Speaker(Seat speaker, Seat viewer) => Arena(speaker == viewer ? "who.self" : "who.other");

    private void AppendSection(StringBuilder sb, string headingKey, string body) =>
        sb.Append(Arena(headingKey)).Append('\n').Append(body).Append("\n\n");
}
