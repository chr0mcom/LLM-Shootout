using System.Text.Json.Nodes;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Arena.Players;

/// <summary>Role of a chat message in a prompt.</summary>
public enum PromptRole
{
    /// <summary>Rules and general instructions.</summary>
    System,

    /// <summary>What the player sees and is asked.</summary>
    User,

    /// <summary>An earlier answer of the player, used for correction hints.</summary>
    Assistant,
}

/// <summary>One message of a prompt.</summary>
public sealed record PromptMessage(PromptRole Role, string Text);

/// <summary>Why the arena calls a player.</summary>
public enum CallPurpose
{
    /// <summary>A turn of the game.</summary>
    Turn,

    /// <summary>The debriefing after the game.</summary>
    Debrief,

    /// <summary>A judge model rating an existing protocol.</summary>
    Judge,
}

/// <summary>
/// One request to a player. It contains everything a player needs and nothing about the game: the finished prompt,
/// a JSON schema of the expected answer, and a way to produce a valid answer for players that do not think.
/// </summary>
/// <param name="Purpose">Why the player is called.</param>
/// <param name="Messages">The prompt.</param>
/// <param name="Schema">JSON schema of the expected answer, or null if none can be given.</param>
/// <param name="Attempt">1 for the first try, then 2 and 3 for retries.</param>
/// <param name="SampleValidAnswer">Produces a valid answer as JSON text using the given generator.</param>
public sealed record PlayerCall(
    CallPurpose Purpose,
    IReadOnlyList<PromptMessage> Messages,
    JsonNode? Schema,
    int Attempt,
    Func<SeededRng, string> SampleValidAnswer);

/// <summary>The unparsed answer of a player.</summary>
/// <param name="Content">The text of the answer. May be null or empty for reasoning models.</param>
/// <param name="Reasoning">The reasoning channel of the model, if delivered.</param>
/// <param name="Call">Technical details of the model call, or null for players that are not models.</param>
/// <param name="RawExchange">The raw HTTP request and response without credentials, or null.</param>
/// <param name="Notice">A message of the endpoint about this answer, for example an endpoint that could not produce structured output. It is written to the protocol, never shown to the other player or to the model.</param>
public sealed record PlayerReply(string? Content, string? Reasoning = null, ModelCallInfo? Call = null, string? RawExchange = null, string? Notice = null);

/// <summary>Technical details of one model call, copied into the protocol.</summary>
public sealed record ModelCallInfo(
    string RequestedModel,
    string? DeliveredModel,
    string? Provider,
    string? GenerationId,
    int? PromptTokens,
    int? CompletionTokens,
    int? ReasoningTokens,
    decimal? CostUsd,
    TimeSpan Latency,
    string? FinishReason,
    int HttpAttempts,
    decimal? EstimatedCostUsd = null)
{
    /// <summary>True if the endpoint reported no cost and none could be estimated: the call is held back with a reserve against the spending limit.</summary>
    public bool CostIsUnknown() => CostUsd is null && EstimatedCostUsd is null;
}

/// <summary>Anything that can answer for a seat: a language model, a random player, a script, a human.</summary>
public interface IPlayer
{
    /// <summary>Short name for logs and reports, for example <c>openrouter:openai/gpt-4o</c> or <c>random</c>.</summary>
    string Name { get; }

    /// <summary>The model behind the player, or null if it is not a model.</summary>
    ModelIdentity? Model { get; }

    /// <summary>The settings sent with every call (temperature, reasoning effort, output limit, timeout), recorded in the protocol so that a repeated debriefing can use them. Empty by default.</summary>
    IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>();

    /// <summary>Answers a request. Throws <see cref="PlayerFailureException"/> on technical failure.</summary>
    Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken);
}

/// <summary>Technical failure of a player (timeout, HTTP error, rate limit) after all HTTP retries.</summary>
public sealed class PlayerFailureException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PlayerFailureException(string message, bool isTimeout = false, Exception? inner = null)
        : base(message, inner) => IsTimeout = isTimeout;

    /// <summary>Creates an empty exception.</summary>
    public PlayerFailureException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public PlayerFailureException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public PlayerFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>True if the call timed out. A timeout uses up one attempt; other failures abort the match.</summary>
    public bool IsTimeout { get; }

    /// <summary>What is known about the failed call (requested model, HTTP attempts), or null.</summary>
    public ModelCallInfo? Call { get; init; }

    /// <summary>The raw exchange of the failed call without credentials, or null.</summary>
    public string? RawExchange { get; init; }
}
