using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Protocol;

/// <summary>The values of <c>model_call.class</c>: how one answer was classified.</summary>
public static class CallClass
{
    /// <summary>Format and move are fine.</summary>
    public const string Valid = "valid";

    /// <summary>Not usable: no JSON, a field missing or forbidden, too long, or the move breaks the rules.</summary>
    public const string Invalid = "invalid";

    /// <summary>The text declines to play or moralizes.</summary>
    public const string Refusal = "refusal";

    /// <summary>The answer was cut off at the output limit (<c>finish_reason: length</c>); not a rule violation.</summary>
    public const string TokenLimit = "token_limit";

    /// <summary>The call failed (timeout, HTTP error) and there is no answer.</summary>
    public const string TechnicalError = "technical_error";

    /// <summary>The class of an evaluated answer.</summary>
    public static string Of(AnswerClass answer) => answer switch
    {
        AnswerClass.Valid => Valid,
        AnswerClass.Invalid => Invalid,
        AnswerClass.Refusal => Refusal,
        _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, "Unknown answer class."),
    };
}

/// <summary>The values of <c>turn.status</c>.</summary>
public static class TurnStatus
{
    /// <summary>The seat gave a valid answer.</summary>
    public const string Played = "played";

    /// <summary>The seat used up its attempts; the game decides what follows.</summary>
    public const string Exhausted = "exhausted";

    /// <summary>The status of a submission.</summary>
    public static string Of(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Played => Played,
        SubmissionStatus.Exhausted => Exhausted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown submission status."),
    };
}

/// <summary>The values of <c>match_aborted.reason</c>.</summary>
public static class AbortReason
{
    /// <summary>The spending limit was reached.</summary>
    public const string CostLimit = "cost_limit";

    /// <summary>A call timed out in every attempt of a turn.</summary>
    public const string Timeout = "timeout";

    /// <summary>A player failed technically (HTTP error after all retries).</summary>
    public const string PlayerFailure = "player_failure";

    /// <summary>The run was cancelled (Ctrl+C, or the cancel button of the web page).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>An unexpected exception.</summary>
    public const string InternalError = "internal_error";
}
