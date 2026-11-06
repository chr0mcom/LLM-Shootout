using System.ComponentModel.DataAnnotations;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>Settings of the connection to OpenRouter.</summary>
public sealed record OpenRouterOptions
{
    /// <summary>Name of the environment variable that holds the API key.</summary>
    public const string ApiKeyVariable = "OPENROUTER_API_KEY";

    /// <summary>Base address of the OpenAI-compatible API.</summary>
    public Uri BaseAddress { get; init; } = new("https://openrouter.ai/api/v1/");

    /// <summary>The API key. Never written to files, logs or protocols.</summary>
    public string? ApiKey { get; init; }

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

    /// <summary>Reads the API key from the environment and returns a copy with it set.</summary>
    public OpenRouterOptions WithApiKeyFromEnvironment() =>
        this with { ApiKey = Environment.GetEnvironmentVariable(ApiKeyVariable) };
}
