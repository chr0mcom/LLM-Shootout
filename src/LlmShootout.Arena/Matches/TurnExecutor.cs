using System.Text.Json.Nodes;
using LlmShootout.Arena.Answers;
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
/// <param name="TokenLimitHits">Number of answers cut off by the token limit.</param>
/// <param name="Estimate">The self-assessment from 0 to 100, or null if none was asked for or none was usable.</param>
/// <param name="ViolationCode">The code of the broken rule if the accepted move was a rule violation, otherwise null.</param>
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
    int Refusals,
    int TokenLimitHits,
    int? Estimate = null,
    string? ViolationCode = null);

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
        EnsureEstimateIsDescribed(turn);
        var max = context.Plan.Options.MaxAttempts;
        var state = new TurnState();

        for (var attempt = 1; attempt <= max; attempt++)
        {
            MatchLog.Attempt(logger, turn.TurnId, turn.Seat, attempt);
            var answer = await TryAttemptAsync(turn, attempt, attempt == max, state, cancellationToken);
            if (answer is { IsValid: true })
            {
                return Finish(turn, state, answer, SubmissionStatus.Played, attempt);
            }
        }

        if (state.NeverAnswered)
        {
            throw new PlayerFailureException($"All {max} attempts of turn '{turn.TurnId}' timed out.", isTimeout: true);
        }

        return Finish(turn, state, state.LastEvaluation!, SubmissionStatus.Exhausted, max);
    }

    private static void EnsureEstimateIsDescribed(TurnRequest turn)
    {
        if (turn.Estimate != FieldRule.Forbidden && turn.EstimateMeaning is null)
        {
            throw new ArgumentException($"Turn '{turn.TurnId}' asks for an estimate but names no meaning for it.", nameof(turn));
        }
    }

    /// <summary>One model call and the classification of its answer. Returns the evaluation, or null if the call timed out.</summary>
    private async Task<Evaluation?> TryAttemptAsync(TurnRequest turn, int attempt, bool isLast, TurnState state, CancellationToken cancellationToken)
    {
        var request = BuildRequest(turn, attempt, state);
        var reply = await CallPlayerAsync(turn, request, cancellationToken);
        return reply is null ? null : await EvaluateAsync(turn, request, reply, isLast, state, cancellationToken);
    }

    private PlayerCall BuildRequest(TurnRequest turn, int attempt, TurnState state)
    {
        var messages = context.Prompts.BuildTurn(new TurnPromptInput(
            turn.Seat,
            context.Game,
            turn,
            context.Timeline,
            context.Thoughts,
            state.PreviousAnswer,
            state.Hint,
            state.AfterRefusal));
        return new PlayerCall(CallPurpose.Turn, messages, ResponseSchema.ForTurn(turn, context.Plan.Setup.Communication), attempt, rng => SampleAnswer(turn, rng));
    }

    /// <summary>Calls the player. A timeout is written to the protocol and returns null; any other failure ends the match.</summary>
    private async Task<PlayerReply?> CallPlayerAsync(TurnRequest turn, PlayerCall request, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Gateway.CallAsync(context.Plan.Players[turn.Seat], request, cancellationToken);
        }
        catch (PlayerFailureException ex)
        {
            await context.Writer.AppendAsync(ModelCallEvent.Failed(context.Time.GetUtcNow(), turn.TurnId, CallPurpose.Turn, turn.Seat, request.Attempt, request.Messages, ex), cancellationToken);
            if (!ex.IsTimeout)
            {
                throw;
            }

            return null;
        }
    }

    private async Task<Evaluation> EvaluateAsync(TurnRequest turn, PlayerCall request, PlayerReply reply, bool isLast, TurnState state, CancellationToken cancellationToken)
    {
        var parsed = AnswerParser.Parse(reply.Content, reply.Reasoning);
        var evaluation = context.Evaluator.Evaluate(turn, parsed, string.IsNullOrWhiteSpace(reply.Content) ? reply.Reasoning : reply.Content, context.Game, isLast);
        state.LastEvaluation = evaluation;
        var tokenLimit = !evaluation.IsValid && reply.Call?.FinishReason == "length";
        await WriteCallAsync(turn, request, reply, parsed, evaluation, tokenLimit, cancellationToken);
        MatchLog.Classified(logger, turn.TurnId, turn.Seat, request.Attempt, evaluation.Class);

        if (!evaluation.IsValid)
        {
            var hint = tokenLimit ? context.Prompts.Arena("hint.length") : CorrectionHintBuilder.Build(evaluation.Violations, evaluation.ExtraKeys, _hints);
            state.RegisterFailure(evaluation, reply, hint, context.Prompts.Arena("answer.empty"), tokenLimit);
        }

        return evaluation;
    }

    private TurnResult Finish(TurnRequest turn, TurnState state, Evaluation evaluation, SubmissionStatus status, int attempts)
    {
        if (evaluation.Thought is { } thought)
        {
            context.Thoughts.Add(turn.Seat, thought);
        }

        var played = status == SubmissionStatus.Played;
        return new TurnResult(
            new TurnSubmission(turn.TurnId, turn.Seat, played ? evaluation.Move : null, status, played ? evaluation.MoveKind : MoveKind.Regular),
            turn.Message != FieldRule.Forbidden,
            evaluation.Message,
            evaluation.Silent || (turn.Message == FieldRule.Required && evaluation.Message is null),
            evaluation.MessageTruncated,
            evaluation.Thought,
            evaluation.ThoughtTruncated,
            attempts,
            state.InvalidAnswers,
            state.Refusals,
            state.TokenLimitHits,
            evaluation.Estimate,
            played && evaluation.MoveKind == MoveKind.RuleViolation ? evaluation.ViolationCode : null);
    }

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
        if (turn.Estimate != FieldRule.Forbidden)
        {
            answer["estimate"] = rng.NextInt(101);
        }

        return answer.ToJsonString();
    }

    private Task WriteCallAsync(TurnRequest turn, PlayerCall request, PlayerReply reply, ParsedAnswer parsed, Evaluation evaluation, bool tokenLimit, CancellationToken cancellationToken)
    {
        var fields = parsed.HasJson ? new ParsedFields(parsed.Thought, parsed.Message, parsed.Move, parsed.FromReasoning, parsed.ExtraKeys, parsed.Estimate) : null;
        var violations = evaluation.Violations.Select(v => v.Code is null ? $"{v.Key}: {v.Violation}" : $"{v.Key} [{v.Code}]: {v.Violation}").ToList();
        if (evaluation.DiscardedEstimate is { } discarded)
        {
            violations.Add($"{discarded.Key} [discarded]: {discarded.Violation}");
        }

        var callClass = tokenLimit ? CallClass.TokenLimit : CallClass.Of(evaluation.Class);
        return context.Writer.AppendAsync(
            ModelCallEvent.Answered(context.Time.GetUtcNow(), turn.TurnId, CallPurpose.Turn, turn.Seat, request.Attempt, request.Messages, reply, fields, callClass, violations),
            cancellationToken);
    }

    private sealed class TurnState
    {
        public Evaluation? LastEvaluation { get; set; }

        public bool NeverAnswered => LastEvaluation is null;

        public string? PreviousAnswer { get; private set; }

        public string? Hint { get; private set; }

        public bool AfterRefusal { get; private set; }

        public int InvalidAnswers { get; private set; }

        public int Refusals { get; private set; }

        public int TokenLimitHits { get; private set; }

        public void RegisterFailure(Evaluation evaluation, PlayerReply reply, string hint, string emptyText, bool tokenLimit)
        {
            PreviousAnswer = string.IsNullOrWhiteSpace(reply.Content) ? emptyText : reply.Content;
            Hint = hint;
            AfterRefusal = !tokenLimit && evaluation.Class == AnswerClass.Refusal;
            if (tokenLimit)
            {
                TokenLimitHits++;
            }
            else if (AfterRefusal)
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
