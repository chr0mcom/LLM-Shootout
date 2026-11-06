using System.Globalization;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using Microsoft.Extensions.Logging;

namespace LlmShootout.Arena.Matches;

/// <summary>What one seat did in one turn.</summary>
/// <param name="Submission">The move (or exhaustion) for the game.</param>
/// <param name="MessageAllowed">True if the turn allowed a public message.</param>
/// <param name="Message">The public message, or null for silence.</param>
/// <param name="Silent">True if a required message stayed out.</param>
/// <param name="MessageTruncated">True if the message was shortened.</param>
/// <param name="Thought">The private thought, if any.</param>
/// <param name="ThoughtTruncated">True if the thought was shortened.</param>
/// <param name="Attempts">Model calls used.</param>
/// <param name="InvalidAnswers">Number of invalid answers.</param>
/// <param name="Refusals">Number of refusals.</param>
public sealed record TurnResult(
    TurnSubmission Submission,
    bool MessageAllowed,
    string? Message,
    bool Silent,
    bool MessageTruncated,
    string? Thought,
    bool ThoughtTruncated,
    int Attempts,
    int InvalidAnswers,
    int Refusals);

/// <summary>
/// Plays one turn of one seat: builds the prompt, calls the player, classifies the answer, and retries with a
/// correction hint until the attempt budget is used up.
/// </summary>
public sealed class TurnExecutor(MatchContext context, ILogger logger)
{
    private readonly HintTexts _hints = new(
        context.Prompts.Arena("hint.header"),
        context.Prompts.Arena("hint.allowed"),
        context.Prompts.Arena("hint.unknown"),
        context.Prompts.Arena("hint.generic"));

    /// <summary>Runs the turn.</summary>
    public async Task<TurnResult> ExecuteAsync(TurnRequest turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var max = context.Plan.Options.MaxAttempts;
        var state = new TurnState();

        for (var attempt = 1; attempt <= max; attempt++)
        {
            MatchLog.Attempt(logger, turn.TurnId, turn.Seat, attempt);
            context.Cost.EnsureWithinLimit();
            var evaluation = await TryAttemptAsync(turn, attempt, attempt == max, state, cancellationToken);
            if (evaluation?.IsValid == true)
            {
                return Finish(turn, state, evaluation, SubmissionStatus.Played, attempt);
            }
        }

        if (state.LastEvaluation is null)
        {
            throw new PlayerFailureException($"All {max} attempts of turn '{turn.TurnId}' timed out.", isTimeout: true);
        }

        return Finish(turn, state, state.LastEvaluation, SubmissionStatus.Exhausted, max);
    }

    private async Task<Evaluation?> TryAttemptAsync(TurnRequest turn, int attempt, bool isLast, TurnState state, CancellationToken cancellationToken)
    {
        var messages = context.Prompts.BuildTurn(new TurnPromptInput(
            turn.Seat,
            context.Game,
            turn,
            context.Timeline,
            context.Thoughts,
            OpponentModel(turn.Seat),
            state.PreviousAnswer,
            state.Hint,
            state.AfterRefusal));
        var request = new PlayerCall(CallPurpose.Turn, messages, ResponseSchema.ForTurn(turn), attempt, rng => SampleAnswer(turn, rng));
        var started = context.Time.GetTimestamp();

        PlayerReply reply;
        try
        {
            reply = await context.Plan.Players[turn.Seat].RespondAsync(request, cancellationToken);
        }
        catch (PlayerFailureException ex)
        {
            context.Cost.Record(null);
            await WriteFailureAsync(turn, attempt, messages, ex, cancellationToken);
            if (ex.IsTimeout)
            {
                return null;
            }

            throw;
        }

        context.Cost.Record(reply.Call is null ? 0m : reply.Call.CostUsd);
        var parsed = AnswerParser.Parse(reply.Content, reply.Reasoning);
        var evaluation = context.Evaluator.Evaluate(turn, parsed, reply.Content, context.Game, isLast);
        state.LastEvaluation = evaluation;
        await WriteCallAsync(turn, attempt, messages, reply, parsed, evaluation, started, cancellationToken);
        MatchLog.Classified(logger, turn.TurnId, turn.Seat, attempt, evaluation.Class);

        if (!evaluation.IsValid)
        {
            state.RegisterFailure(evaluation, reply, CorrectionHintBuilder.Build(evaluation.Violations, evaluation.ExtraKeys, _hints), context.Prompts.Arena("answer.empty"));
        }

        return evaluation;
    }

    private TurnResult Finish(TurnRequest turn, TurnState state, Evaluation evaluation, SubmissionStatus status, int attempts)
    {
        if (evaluation.Thought is { } thought)
        {
            context.Thoughts.Add(turn.Seat, thought);
        }

        return new TurnResult(
            new TurnSubmission(turn.TurnId, turn.Seat, status == SubmissionStatus.Played ? evaluation.Move : null, status),
            turn.Message != FieldRule.Forbidden,
            evaluation.Message,
            evaluation.Silent || (turn.Message == FieldRule.Required && evaluation.Message is null),
            evaluation.MessageTruncated,
            evaluation.Thought,
            evaluation.ThoughtTruncated,
            attempts,
            state.InvalidAnswers,
            state.Refusals);
    }

    private Identity.ModelIdentity? OpponentModel(Seat seat) =>
        context.Plan.Options.RevealIdentity ? context.Plan.Players[seat.Opponent].Model : null;

    private string SampleAnswer(TurnRequest turn, SeededRng rng)
    {
        var messages = context.Prompts.Arena("sample.messages").Split('\n');
        var wantsMessage = turn.Message == FieldRule.Required || (turn.Message == FieldRule.Optional && rng.NextBool(0.5));
        var answer = new JsonObject
        {
            ["thought"] = turn.Thought == FieldRule.Forbidden ? null : context.Prompts.Arena("sample.thought"),
            ["message"] = wantsMessage ? messages[rng.NextInt(messages.Length)] : null,
            ["move"] = turn.Move == FieldRule.Forbidden ? null : context.Game.SampleLegalMove(turn, rng),
        };
        return answer.ToJsonString();
    }

    private Task WriteFailureAsync(TurnRequest turn, int attempt, IReadOnlyList<PromptMessage> messages, PlayerFailureException ex, CancellationToken cancellationToken) =>
        context.Writer.AppendAsync(
            new ModelCallEvent(context.Time.GetUtcNow(), turn.TurnId, CallPurpose.Turn, turn.Seat, attempt, messages, null, null, null, "technical_error", [], null, true, ex.Message, null),
            cancellationToken);

    private Task WriteCallAsync(TurnRequest turn, int attempt, IReadOnlyList<PromptMessage> messages, PlayerReply reply, ParsedAnswer parsed, Evaluation evaluation, long started, CancellationToken cancellationToken)
    {
        var info = reply.Call is null ? null : reply.Call with { Latency = reply.Call.Latency == TimeSpan.Zero ? context.Time.GetElapsedTime(started) : reply.Call.Latency };
        var fields = parsed.HasJson ? new ParsedFields(parsed.Thought, parsed.Message, parsed.Move, parsed.FromReasoning, parsed.ExtraKeys) : null;
        var violations = evaluation.Violations.Select(v => $"{v.Key}: {v.Violation}").ToArray();
        return context.Writer.AppendAsync(
            new ModelCallEvent(
                context.Time.GetUtcNow(),
                turn.TurnId,
                CallPurpose.Turn,
                turn.Seat,
                attempt,
                messages,
                reply.Content,
                reply.Reasoning,
                fields,
                evaluation.Class.ToString().ToLowerInvariant(),
                violations,
                info,
                reply.Call is { CostUsd: null },
                null,
                reply.RawExchange),
            cancellationToken);
    }

    private sealed class TurnState
    {
        public Evaluation? LastEvaluation { get; set; }

        public string? PreviousAnswer { get; private set; }

        public string? Hint { get; private set; }

        public bool AfterRefusal { get; private set; }

        public int InvalidAnswers { get; private set; }

        public int Refusals { get; private set; }

        public void RegisterFailure(Evaluation evaluation, PlayerReply reply, string hint, string emptyText)
        {
            PreviousAnswer = string.IsNullOrWhiteSpace(reply.Content) ? emptyText : reply.Content;
            Hint = hint;
            AfterRefusal = evaluation.Class == AnswerClass.Refusal;
            if (AfterRefusal)
            {
                Refusals++;
            }
            else
            {
                InvalidAnswers++;
            }
        }
    }
}
