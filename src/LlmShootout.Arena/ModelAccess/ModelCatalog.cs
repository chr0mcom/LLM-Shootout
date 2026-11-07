using System.Globalization;
using System.Text.Json.Nodes;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>A model from the model list of an endpoint.</summary>
/// <param name="Id">The slug, for example <c>openai/gpt-4o</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="PromptUsdPerMillion">Input price in USD per million tokens.</param>
/// <param name="CompletionUsdPerMillion">Output price in USD per million tokens.</param>
/// <param name="ContextLength">Context window in tokens.</param>
/// <param name="SupportsJsonSchema">True if <c>supported_parameters</c> lists <c>structured_outputs</c>.</param>
/// <param name="SupportsReasoning">True if <c>supported_parameters</c> lists <c>reasoning</c>.</param>
/// <param name="SupportedParameters">All entries of <c>supported_parameters</c>.</param>
public sealed record ModelInfo(
    string Id,
    string Name,
    decimal PromptUsdPerMillion,
    decimal CompletionUsdPerMillion,
    int ContextLength,
    bool SupportsJsonSchema,
    bool SupportsReasoning,
    IReadOnlySet<string> SupportedParameters);

/// <summary>
/// The model list of an endpoint (<c>GET models</c>; OpenRouter needs no key) with a short file cache, so that repeated
/// commands do not hit the API and offline runs can still use the last list. Other OpenAI-compatible endpoints list
/// only ids: prices are 0 and every model counts as capable of JSON schema output.
/// </summary>
public sealed class ModelCatalog(HttpClient http, EndpointOptions options, TimeProvider time)
{
    private string CacheFile => options.Kind == EndpointKind.OpenRouter ? "models.json" : $"models.{CacheName(options.EndpointName)}.json";

    /// <summary>Creates the model list of an endpoint. A key is sent to endpoints other than OpenRouter, whose lists need no key.</summary>
    public static ModelCatalog ForEndpoint(EndpointOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        var http = new HttpClient { BaseAddress = options.BaseAddress, Timeout = ListTimeout };
        if (options.Kind != EndpointKind.OpenRouter && !string.IsNullOrEmpty(options.ApiKey))
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        return new ModelCatalog(http, options, time);
    }

    /// <summary>How long one request for the model list may take.</summary>
    public static TimeSpan ListTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a cached list is used without asking the endpoint again.</summary>
    public static TimeSpan CacheLifetime { get; } = TimeSpan.FromHours(1);

    /// <summary>Loads the list from the cache or from the endpoint. Falls back to an older cache if the network fails.</summary>
    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.CacheDirectory, CacheFile);
        if (File.Exists(path) && time.GetUtcNow() - File.GetLastWriteTimeUtc(path) < CacheLifetime)
        {
            return Parse(await File.ReadAllTextAsync(path, cancellationToken), options.Kind);
        }

        try
        {
            var json = await http.GetStringAsync(new Uri("models", UriKind.Relative), cancellationToken);
            Directory.CreateDirectory(options.CacheDirectory);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            return Parse(json, options.Kind);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && File.Exists(path))
        {
            return Parse(await File.ReadAllTextAsync(path, cancellationToken), options.Kind);
        }
    }

    /// <summary>Finds a model by slug, or null.</summary>
    public async Task<ModelInfo?> FindAsync(string slug, CancellationToken cancellationToken) =>
        (await GetModelsAsync(cancellationToken)).FirstOrDefault(m => string.Equals(m.Id, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses a response of the models endpoint.</summary>
    public static IReadOnlyList<ModelInfo> Parse(string json, EndpointKind kind = EndpointKind.OpenRouter)
    {
        var generic = kind != EndpointKind.OpenRouter;
        var data = JsonNode.Parse(json)?["data"]?.AsArray() ?? [];
        return
        [
            .. data.OfType<JsonObject>().Select(m =>
            {
                var supported = m["supported_parameters"]?.AsArray().Select(p => p?.ToString()).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];
                return new ModelInfo(
                    m["id"]?.ToString() ?? string.Empty,
                    m["name"]?.ToString() ?? string.Empty,
                    PerMillion(m["pricing"]?["prompt"]),
                    PerMillion(m["pricing"]?["completion"]),
                    int.TryParse(m["context_length"]?.ToString(), CultureInfo.InvariantCulture, out var context) ? context : 0,
                    generic || supported.Contains("structured_outputs"),
                    supported.Contains("reasoning"),
                    supported);
            }).Where(m => m.Id.Length > 0),
        ];
    }

    private static string CacheName(string endpoint) =>
        new([.. endpoint.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);

    private static decimal PerMillion(JsonNode? perToken) =>
        decimal.TryParse(perToken?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * 1_000_000m : 0m;
}
