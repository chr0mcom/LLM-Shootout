using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>
/// What the HTTP layer saw during one model call: the request and response bodies without credentials,
/// the number of HTTP attempts, and the fields of the response (cost, provider, reasoning) that the chat abstraction does not carry.
/// </summary>
public sealed class CallCapture
{
    private static readonly AsyncLocal<CallCapture?> CurrentCapture = new();

    /// <summary>The capture of the call running on this async flow, or null.</summary>
    public static CallCapture? Current => CurrentCapture.Value;

    /// <summary>Number of HTTP requests sent for this call, including retries.</summary>
    public int HttpAttempts { get; internal set; }

    /// <summary>The last request body as sent.</summary>
    public string? RequestBody { get; internal set; }

    /// <summary>The last response body as received.</summary>
    public string? ResponseBody { get; internal set; }

    /// <summary>The last HTTP status code.</summary>
    public int? StatusCode { get; internal set; }

    /// <summary>Starts a capture for the current async flow. Dispose the result to end it.</summary>
    public static Scope Begin()
    {
        var previous = CurrentCapture.Value;
        var capture = new CallCapture();
        CurrentCapture.Value = capture;
        return new Scope(capture, previous);
    }

    /// <summary>The raw exchange as text for the protocol. Contains no headers, so no credentials.</summary>
    public string? RenderExchange() => RequestBody is null
        ? null
        : $"POST (attempts: {HttpAttempts}, status: {StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"})\n--- request ---\n{RequestBody}\n--- response ---\n{ResponseBody}";

    /// <summary>The fields of the response body that the chat abstraction does not carry.</summary>
    public CompletionFields Fields() => CompletionFields.Parse(ResponseBody);

    /// <summary>Ends the capture when disposed.</summary>
    public readonly struct Scope(CallCapture capture, CallCapture? previous) : IDisposable
    {
        /// <summary>The capture.</summary>
        public CallCapture Capture => capture;

        /// <inheritdoc />
        public void Dispose() => CurrentCapture.Value = previous;
    }
}

/// <summary>Fields of a chat completion that matter for the experiment. Cost and provider are OpenRouter specific; other endpoints leave them out.</summary>
public sealed record CompletionFields(
    string? GenerationId,
    string? Model,
    string? Provider,
    decimal? CostUsd,
    int? PromptTokens,
    int? CompletionTokens,
    int? ReasoningTokens,
    string? FinishReason,
    string? Content,
    string? Reasoning,
    string? ErrorMessage = null,
    string? Refusal = null)
{
    /// <summary>Reads the fields from a response body. Missing or malformed bodies give an empty result.</summary>
    public static CompletionFields Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new CompletionFields(null, null, null, null, null, null, null, null, null, null);
        }

        try
        {
            return FromNode(JsonNode.Parse(body) as JsonObject);
        }
        catch (System.Text.Json.JsonException)
        {
            return new CompletionFields(null, null, null, null, null, null, null, null, null, null);
        }
    }

    private static CompletionFields FromNode(JsonObject? root)
    {
        var choice = root?["choices"]?[0];
        var message = choice?["message"];
        var usage = root?["usage"];
        return new CompletionFields(
            root?["id"]?.ToString(),
            root?["model"]?.ToString(),
            root?["provider"]?.ToString(),
            ReadDecimal(usage?["cost"]),
            ReadInt(usage?["prompt_tokens"]),
            ReadInt(usage?["completion_tokens"]),
            ReadInt(usage?["completion_tokens_details"]?["reasoning_tokens"]),
            choice?["finish_reason"]?.ToString(),
            message?["content"]?.ToString(),
            message?["reasoning"]?.ToString() ?? message?["reasoning_content"]?.ToString(),
            root?["error"]?["message"]?.ToString() ?? choice?["error"]?["message"]?.ToString(),
            message?["refusal"]?.ToString());
    }

    private static decimal? ReadDecimal(JsonNode? node) =>
        node is not null && decimal.TryParse(node.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? ReadInt(JsonNode? node) =>
        node is not null && int.TryParse(node.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>Records request and response of every HTTP attempt into the current <see cref="CallCapture"/>. Never records headers.</summary>
public sealed class RawCaptureHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capture = CallCapture.Current;
        if (capture is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        capture.HttpAttempts++;
        capture.StatusCode = null;
        capture.ResponseBody = null;
        capture.RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        capture.StatusCode = (int)response.StatusCode;
        capture.ResponseBody = await ReadAndBufferAsync(response, cancellationToken);
        return response;
    }

    private static async Task<string> ReadAndBufferAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var text = Encoding.UTF8.GetString(bytes);
        var content = new ByteArrayContent(bytes);
        foreach (var header in response.Content.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = content;
        return text;
    }
}
