using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.AI;
using Polly.Timeout;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>
/// A player backed by a language model reached through an <see cref="IChatClient"/>. Its name is
/// <c>&lt;endpoint&gt;:&lt;model&gt;</c>. Without a reported cost, the call counts as 0 USD on a flat-rate endpoint and
/// is estimated from the model price otherwise.
/// </summary>
public sealed class ChatPlayer(IChatClient client, string modelSlug, EndpointOptions options, bool useJsonSchema, TimeProvider time, ModelInfo? pricing = null) : IPlayer
{
    private const int MaxErrorTextLength = 300;

    /// <inheritdoc />
    public string Name => $"{options.EndpointName}:{modelSlug}";

    /// <inheritdoc />
    public ModelIdentity? Model { get; private set; } = new ModelIdentity(modelSlug);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Settings { get; } = DescribeSettings(options);

    /// <inheritdoc />
    public async Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = time.GetTimestamp();
        using var scope = CallCapture.Begin();
        try
        {
            var response = await client.GetResponseAsync(ToMessages(request), ToOptions(request), cancellationToken);
            return ToReply(response, scope.Capture, time.GetElapsedTime(started));
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Failure($"Call to '{modelSlug}' timed out after {options.CallTimeout.TotalSeconds:0} s.", ex, scope.Capture, started, isTimeout: true);
        }
        catch (ClientResultException ex)
        {
            var what = ex.Status == 0 ? $"Connection to {Label} failed" : $"{Label} returned HTTP {ex.Status}";
            throw Failure($"{what} for '{modelSlug}': {Shorten(ex.Message)}", ex, scope.Capture, started);
        }
        catch (HttpRequestException ex)
        {
            throw Failure($"Connection to {Label} failed for '{modelSlug}': {ex.Message}", ex, scope.Capture, started);
        }
    }

    /// <summary>A failed call, with what is known about it and the raw exchange (without credentials).</summary>
    private PlayerFailureException Failure(string message, Exception inner, CallCapture capture, long started, bool isTimeout = false) =>
        new(message, isTimeout, inner)
        {
            Call = FailedCall(capture, time.GetElapsedTime(started)),
            RawExchange = capture.RenderExchange(),
        };

    private static Dictionary<string, string> DescribeSettings(EndpointOptions options)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlayerSettingKeys.CallTimeoutSeconds] = options.CallTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture),
        };
        if (options.Temperature is { } temperature)
        {
            settings[PlayerSettingKeys.Temperature] = temperature.ToString(CultureInfo.InvariantCulture);
        }

        if (options.ReasoningEffort is { } effort)
        {
            settings[PlayerSettingKeys.ReasoningEffort] = effort;
        }

        if (options.MaxOutputTokens is { } tokens)
        {
            settings[PlayerSettingKeys.MaxOutputTokens] = tokens.ToString(CultureInfo.InvariantCulture);
        }

        return settings;
    }

    private static List<ChatMessage> ToMessages(PlayerCall request) =>
    [
        .. request.Messages.Select(m => new ChatMessage(
            m.Role switch
            {
                PromptRole.System => ChatRole.System,
                PromptRole.Assistant => ChatRole.Assistant,
                _ => ChatRole.User,
            },
            m.Text)),
    ];

    private ChatOptions ToOptions(PlayerCall request)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = modelSlug,
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens,
        };

        if (useJsonSchema && request.Schema is not null)
        {
            var schema = JsonSerializer.SerializeToElement(request.Schema);
            chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "answer", "The answer of one turn");
        }

        return chatOptions;
    }

    private PlayerReply ToReply(ChatResponse response, CallCapture capture, TimeSpan latency)
    {
        var fields = capture.Fields();
        var info = BuildCallInfo(response, fields, capture, latency);
        if (info.DeliveredModel is not null)
        {
            Model = new ModelIdentity(modelSlug, info.DeliveredModel);
        }

        if (fields.ErrorMessage is not null || info.FinishReason == "error")
        {
            throw new PlayerFailureException($"{Label} reported an error for '{modelSlug}': {Shorten(fields.ErrorMessage ?? "finish_reason error")}")
            {
                Call = info,
                RawExchange = capture.RenderExchange(),
            };
        }

        var content = string.IsNullOrWhiteSpace(fields.Content) ? response.Text : fields.Content;
        var hasContent = !string.IsNullOrWhiteSpace(content);
        var notice = !hasContent && !string.IsNullOrWhiteSpace(fields.Refusal) ? Shorten(fields.Refusal) : null;
        return new PlayerReply(hasContent ? content : null, fields.Reasoning, info, capture.RenderExchange(), notice);
    }

    /// <summary>What the response body says wins; the chat abstraction fills what the body does not carry.</summary>
    private ModelCallInfo BuildCallInfo(ChatResponse response, CompletionFields fields, CallCapture capture, TimeSpan latency)
    {
        var promptTokens = fields.PromptTokens ?? (int?)response.Usage?.InputTokenCount;
        var completionTokens = fields.CompletionTokens ?? (int?)response.Usage?.OutputTokenCount;
        return new ModelCallInfo(
            modelSlug,
            fields.Model ?? response.ModelId,
            fields.Provider,
            fields.GenerationId ?? response.ResponseId,
            promptTokens,
            completionTokens,
            fields.ReasoningTokens,
            fields.CostUsd,
            latency,
            fields.FinishReason ?? response.FinishReason?.Value,
            Math.Max(capture.HttpAttempts, 1),
            fields.CostUsd is null ? Estimate(promptTokens, completionTokens) : null);
    }

    private string Label => options.Kind == EndpointKind.OpenRouter ? "OpenRouter" : $"endpoint '{options.EndpointName}'";

    private decimal? Estimate(int? promptTokens, int? completionTokens)
    {
        if (options.FlatRate)
        {
            return 0m;
        }

        return pricing is null || promptTokens is null || completionTokens is null
            ? null
            : ((promptTokens.Value * pricing.PromptUsdPerMillion) + (completionTokens.Value * pricing.CompletionUsdPerMillion)) / 1_000_000m;
    }

    private ModelCallInfo FailedCall(CallCapture capture, TimeSpan latency) =>
        new(modelSlug, null, null, null, null, null, null, null, latency, null, Math.Max(capture.HttpAttempts, 1), options.FlatRate ? 0m : null);

    private static string Shorten(string text) => text.Length <= MaxErrorTextLength ? text : text[..MaxErrorTextLength];
}
