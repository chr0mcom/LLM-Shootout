using System.ClientModel;
using System.Text.Json;
using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.AI;
using Polly.Timeout;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>A player backed by a language model reached through an <see cref="IChatClient"/>.</summary>
public sealed class ChatPlayer(IChatClient client, string modelSlug, OpenRouterOptions options, bool useJsonSchema, TimeProvider time, ModelInfo? pricing = null) : IPlayer
{
    /// <inheritdoc />
    public string Name => $"openrouter:{modelSlug}";

    /// <inheritdoc />
    public ModelIdentity? Model { get; private set; } = new ModelIdentity(modelSlug);

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
            throw new PlayerFailureException($"Call to '{modelSlug}' timed out after {options.CallTimeout.TotalSeconds:0} s.", isTimeout: true, inner: ex)
            {
                Call = FailedCall(scope.Capture, time.GetElapsedTime(started)),
                RawExchange = scope.Capture.RenderExchange(),
            };
        }
        catch (ClientResultException ex)
        {
            var what = ex.Status == 0 ? "Connection to OpenRouter failed" : $"OpenRouter returned HTTP {ex.Status}";
            throw new PlayerFailureException($"{what} for '{modelSlug}': {Shorten(ex.Message)}", ex)
            {
                Call = FailedCall(scope.Capture, time.GetElapsedTime(started)),
                RawExchange = scope.Capture.RenderExchange(),
            };
        }
        catch (HttpRequestException ex)
        {
            throw new PlayerFailureException($"Connection to OpenRouter failed for '{modelSlug}': {ex.Message}", ex)
            {
                Call = FailedCall(scope.Capture, time.GetElapsedTime(started)),
                RawExchange = scope.Capture.RenderExchange(),
            };
        }
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
        var delivered = fields.Model ?? response.ModelId;
        if (delivered is not null)
        {
            Model = new ModelIdentity(modelSlug, delivered);
        }

        var promptTokens = fields.PromptTokens ?? (int?)response.Usage?.InputTokenCount;
        var completionTokens = fields.CompletionTokens ?? (int?)response.Usage?.OutputTokenCount;
        var info = new ModelCallInfo(
            modelSlug,
            delivered,
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
        if (fields.ErrorMessage is not null || info.FinishReason == "error")
        {
            throw new PlayerFailureException($"OpenRouter reported an error for '{modelSlug}': {Shorten(fields.ErrorMessage ?? "finish_reason error")}")
            {
                Call = info,
                RawExchange = capture.RenderExchange(),
            };
        }

        var content = string.IsNullOrWhiteSpace(fields.Content) ? response.Text : fields.Content;
        return new PlayerReply(string.IsNullOrWhiteSpace(content) ? null : content, fields.Reasoning, info, capture.RenderExchange());
    }

    private decimal? Estimate(int? promptTokens, int? completionTokens) =>
        pricing is null || promptTokens is null || completionTokens is null
            ? null
            : ((promptTokens.Value * pricing.PromptUsdPerMillion) + (completionTokens.Value * pricing.CompletionUsdPerMillion)) / 1_000_000m;

    private ModelCallInfo FailedCall(CallCapture capture, TimeSpan latency) =>
        new(modelSlug, null, null, null, null, null, null, null, latency, null, Math.Max(capture.HttpAttempts, 1));

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300];
}
