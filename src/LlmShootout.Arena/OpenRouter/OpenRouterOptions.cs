using System.ComponentModel.DataAnnotations;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>
/// Settings of the connection to one endpoint and of the calls to it. The defaults describe OpenRouter; for another
/// OpenAI-compatible endpoint (see <see cref="EndpointRegistry"/>) address, dialect and key are replaced.
/// </summary>
public sealed record OpenRouterOptions
{
    /// <summary>Name of the environment variable that holds the API key.</summary>
    public const string ApiKeyVariable = "OPENROUTER_API_KEY";

    /// <summary>Name of the endpoint these options belong to. It is the prefix of the player name.</summary>
    public string EndpointName { get; init; } = EndpointRegistry.OpenRouterName;

    /// <summary>The dialect of the endpoint.</summary>
    public EndpointKind Kind { get; init; } = EndpointKind.OpenRouter;

    /// <summary>True if the endpoint is paid by subscription: a call without a reported cost counts as 0 USD.</summary>
    public bool FlatRate { get; init; }

    /// <summary>Base address of the OpenAI-compatible API. A missing trailing slash is added, so that relative paths resolve below it.</summary>
    public Uri BaseAddress
    {
        get;
        init => field = value.AbsoluteUri.EndsWith('/') ? value : new Uri(value.AbsoluteUri + "/");
    } = new("https://openrouter.ai/api/v1/");

    /// <summary>The API key. Never written to files, logs or protocols.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Name of the environment variable the key was read from, for error messages. The value is never stored here.</summary>
    public string ApiKeyVariableName { get; init; } = ApiKeyVariable;

    /// <summary>Time allowed for one model call. Reasoning models can be slow, so the default is generous.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>How often the HTTP layer repeats a call after HTTP 429, 5xx or a connection error.</summary>
    [Range(0, 10)]
    public int MaxHttpRetries { get; init; } = 3;

    /// <summary>Delay before the first HTTP retry; later retries back off exponentially.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Sampling temperature. Null leaves the provider default; it is not sent.</summary>
    public float? Temperature { get; init; }

    /// <summary>Reasoning effort (<c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>). Null leaves the default; it is not sent.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>Limit of output tokens per call. Null leaves the default; it is not sent.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Directory for the cached model list.</summary>
    public string CacheDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "llm-shootout-cache");

    /// <summary>The text of the record without the API key, so that it cannot end up in a log by accident.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"Endpoint = {EndpointName}, BaseAddress = {BaseAddress}, ApiKey = {(string.IsNullOrEmpty(ApiKey) ? "<unset>" : "<set>")}, CallTimeout = {CallTimeout}, MaxHttpRetries = {MaxHttpRetries}");
        return true;
    }

    /// <summary>Reads the API key from the environment and returns a copy with it set.</summary>
    public OpenRouterOptions WithApiKeyFromEnvironment() =>
        this with { ApiKey = Environment.GetEnvironmentVariable(ApiKeyVariable) };
}
