using System.Globalization;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Models;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Arena.Debriefing;

/// <summary>
/// Asks each player about the match after it ended. The player sees exactly what it saw during the game, plus its
/// own thoughts, its own moves and the result. Never the opponent's thoughts, never the opponent's identity if it was hidden.
/// It works from the protocol alone, so the same code runs live and when a debriefing is repeated later.
/// </summary>
public sealed class Debriefer(PlayerGateway gateway, TimeProvider time, int maxRetries = 2)
{
    /// <summary>Debriefs both seats and writes the answers (and the model calls) to <paramref name="writer"/>.</summary>
    public async Task RunAsync(
        IGameDefinition definition,
        MatchRecord match,
        IReadOnlyDictionary<Seat, IPlayer> players,
        MatchOptions options,
        ProtocolWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(writer);

        var started = match.Started;
        var setup = new GameSetup(started.Seed, started.Communication, started.Language, started.FirstSeat, started.GameOptions);
        var rulesGame = definition.Create(setup);
        var prompts = new PromptBuilder(ArenaTemplates.Default, definition.Templates, started.Language, options, started.Communication);
        var questions = StandardQuestions.For(definition.ExtraQuestions(setup), started.Communication, !started.Options.RevealIdentity);
        var session = new Session(definition, match, prompts, questions, rulesGame, BuildTimeline(match));

        foreach (var seat in Seat.All)
        {
            try
            {
                await DebriefSeatAsync(session, seat, players[seat], writer, cancellationToken);
            }
            catch (BudgetExceededException ex)
            {
                await writer.AppendAsync(new DebriefEvent(time.GetUtcNow(), seat, false, new Dictionary<string, string>(), [ex.Message], 0), CancellationToken.None);
            }
        }
    }

    private async Task DebriefSeatAsync(Session s, Seat seat, IPlayer player, ProtocolWriter writer, CancellationToken cancellationToken)
    {
        var messages = new List<PromptMessage>
        {
            new(PromptRole.System, BuildSystem(s, seat)),
            new(PromptRole.User, BuildUser(s, seat)),
        };
        var schema = DebriefAnswers.Schema(s.Questions);
        var problems = new List<string>();
        Dictionary<string, string> answers = [];
        var attempts = 0;

        for (var attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            var request = new PlayerCall(CallPurpose.Debrief, [.. messages], schema, attempt, rng => DebriefAnswers.Sample(s.Questions, rng, s.Prompts.Arena("debrief.sample.text")));
            attempts = attempt;
            PlayerReply reply;
            try
            {
                reply = await gateway.CallAsync(player, request, cancellationToken);
            }
            catch (PlayerFailureException ex)
            {
                await writer.AppendAsync(CallEvent(seat, attempt, request.Messages, null, "technical_error", [], ex.Call, true, ex.Message, ex.RawExchange), cancellationToken);
                problems = [ex.Message];
                break;
            }

            (answers, problems) = Check(s, ReadObject(reply));
            await writer.AppendAsync(CallEvent(seat, attempt, request.Messages, reply, problems.Count == 0 ? "valid" : "invalid", problems, reply.Call, reply.Call is { CostUsd: null }, null, reply.RawExchange), cancellationToken);
            if (problems.Count == 0)
            {
                await writer.AppendAsync(new DebriefEvent(time.GetUtcNow(), seat, true, answers, [], attempt), cancellationToken);
                return;
            }

            messages.Add(new PromptMessage(PromptRole.Assistant, string.IsNullOrWhiteSpace(reply.Content) ? s.Prompts.Arena("answer.empty") : reply.Content));
            messages.Add(new PromptMessage(PromptRole.User, s.Prompts.Arena("debrief.retry", ("hint", Hint(s, problems)))));
        }

        await writer.AppendAsync(new DebriefEvent(time.GetUtcNow(), seat, false, answers, problems, attempts), cancellationToken);
    }

    private static JsonObject? ReadObject(PlayerReply reply)
    {
        foreach (var text in new[] { reply.Content, reply.Reasoning })
        {
            if (ReasoningJsonRecovery.TryExtractJsonObject(text, out var json) && JsonNode.Parse(json) is JsonObject node)
            {
                return node;
            }
        }

        return null;
    }

    private static (Dictionary<string, string> Answers, List<string> Problems) Check(Session s, JsonObject? json)
    {
        if (json is null)
        {
            return ([], [s.Prompts.Arena("hint.generic")]);
        }

        var (answers, violations) = DebriefAnswers.Validate(s.Questions, json);
        return (answers, [.. violations.Select(v => $"{v.Key}: {Explain(s, v)}")]);
    }

    private static string Explain(Session s, (string Key, QuestionKind Kind, string Problem) violation)
    {
        if (violation.Problem == "missing")
        {
            return s.Prompts.Arena("violation.missing");
        }

        var q = s.Questions.FirstOrDefault(x => x.Key == violation.Key);
        return s.Prompts.Arena(
            $"debrief.violation.{violation.Kind.ToString().ToLowerInvariant()}",
            ("min", (q?.Min ?? 1).ToString(CultureInfo.InvariantCulture)),
            ("max", (q?.Max ?? 5).ToString(CultureInfo.InvariantCulture)));
    }

    private static string Hint(Session s, List<string> problems) =>
        s.Prompts.Arena("hint.header") + "\n" + string.Join('\n', problems.Select(p => "- " + p));

    private static string BuildSystem(Session s, Seat seat)
    {
        var opponent = s.Match.Started.Options.RevealIdentity && s.Match.Started.Players[seat.Opponent].RequestedModel is { } model
            ? s.Prompts.Arena("opponent.revealed", ("model", PromptIdentityFormatter.Format(new ModelIdentity(model))))
            : s.Prompts.Arena("opponent.hidden");
        return s.Prompts.Arena("debrief.system", ("opponent", opponent), ("rules", s.Prompts.RenderGame(s.RulesGame.RulesFor(seat))));
    }

    private static string BuildUser(Session s, Seat seat)
    {
        var history = s.Timeline.VisibleTo(seat).Select(e => s.Prompts.RenderEntry(e, seat)).OfType<string>().ToArray();
        var ownMoves = s.Match.Turns(seat).Where(t => t.Move is not null).Select(t => s.Prompts.Arena("debrief.own_move", ("turn", t.TurnId), ("move", t.Move!))).ToArray();
        var thoughts = s.Match.Turns(seat).Select(t => t.Thought).OfType<string>().Select(t => "- " + t).ToArray();
        return s.Prompts.Arena(
            "debrief.user",
            ("history", history.Length == 0 ? s.Prompts.Arena("history.empty") : string.Join('\n', history)),
            ("moves", ownMoves.Length == 0 ? string.Empty : s.Prompts.Arena("debrief.moves", ("moves", string.Join('\n', ownMoves))) + "\n\n"),
            ("notes", thoughts.Length == 0 ? string.Empty : s.Prompts.Arena("debrief.notes", ("thoughts", string.Join('\n', thoughts))) + "\n\n"),
            ("outcome", Outcome(s, seat)),
            ("questions", string.Join('\n', s.Questions.Select(q => Describe(s, q)))),
            ("format", FormatExample(s, s.Questions)));
    }

    private static string Outcome(Session s, Seat seat)
    {
        var result = s.Match.Result;
        if (result is null)
        {
            return s.Prompts.Arena("history.empty");
        }

        return s.Prompts.Arena(
            "debrief.outcome",
            ("outcome", result.Outcome),
            ("own_score", result.Seats[seat].Score.ToString("0.##", CultureInfo.InvariantCulture)),
            ("own_label", result.Seats[seat].Label),
            ("opp_score", result.Seats[seat.Opponent].Score.ToString("0.##", CultureInfo.InvariantCulture)),
            ("opp_label", result.Seats[seat.Opponent].Label));
    }

    private static string Describe(Session s, DebriefingQuestion q)
    {
        var type = s.Prompts.Arena(
            $"debrief.type.{q.Kind.ToString().ToLowerInvariant()}",
            ("min", q.Min.ToString(CultureInfo.InvariantCulture)),
            ("max", q.Max.ToString(CultureInfo.InvariantCulture)),
            ("choices", q.Choices is null ? string.Empty : string.Join(", ", q.Choices)));
        var text = ArenaTemplates.Default.Contains(s.Match.Started.Language, q.Text.Key) ? s.Prompts.Arena(q.Text.Key) : s.Prompts.RenderGame(q.Text);
        var line = s.Prompts.Arena("debrief.question", ("key", q.Key), ("type", type), ("text", text));
        return q.WithReason ? line + "\n" + s.Prompts.Arena("debrief.reason", ("key", q.Key)) : line;
    }

    private static string FormatExample(Session s, IReadOnlyList<DebriefingQuestion> questions)
    {
        string Placeholder(DebriefingQuestion q) => s.Prompts.Arena($"debrief.placeholder.{q.Kind.ToString().ToLowerInvariant()}");
        var members = new List<string>();
        foreach (var q in questions)
        {
            members.Add($"\"{q.Key}\": {Placeholder(q)}");
            if (q.WithReason)
            {
                members.Add($"\"{q.Key}_reason\": {s.Prompts.Arena("debrief.placeholder.text")}");
            }
        }

        return "{" + string.Join(", ", members) + "}";
    }

    private ModelCallEvent CallEvent(
        Seat seat,
        int attempt,
        IReadOnlyList<PromptMessage> messages,
        PlayerReply? reply,
        string cls,
        IReadOnlyList<string> violations,
        ModelCallInfo? call,
        bool costUnknown,
        string? error,
        string? raw) =>
        new(time.GetUtcNow(), $"debrief-{seat}", CallPurpose.Debrief, seat, attempt, messages, reply?.Content, reply?.Reasoning, null, cls, violations, call, costUnknown, error, raw);

    private static Timeline BuildTimeline(MatchRecord match)
    {
        var timeline = new Timeline();
        foreach (var e in match.Events)
        {
            switch (e)
            {
                case MessageEvent m:
                    timeline.AddMessage(m.TurnId, m.Seat, m.Text);
                    break;
                case FactEvent f:
                    timeline.AddFact(f.TurnId, new GameFact(f.Kind, f.Audience, f.Seat, f.Template, f.Data));
                    break;
            }
        }

        return timeline;
    }

    private sealed record Session(
        IGameDefinition Definition,
        MatchRecord Match,
        PromptBuilder Prompts,
        IReadOnlyList<DebriefingQuestion> Questions,
        IGame RulesGame,
        Timeline Timeline);
}
