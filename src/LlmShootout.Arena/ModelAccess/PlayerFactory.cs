using System.Text.Json;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.Logging;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>The player specification could not be turned into a player. The message is meant for the person at the command line.</summary>
public sealed class PlayerSpecException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PlayerSpecException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public PlayerSpecException()
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public PlayerSpecException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Turns a player specification into a player: <c>random</c>, <c>optimal</c> (best moves, for games with an oracle),
/// <c>scripted:&lt;file&gt;</c>,
/// <c>&lt;endpoint&gt;:&lt;model&gt;</c> for an endpoint of the configuration (<c>ollama:llama3.2</c>),
/// <c>openrouter:&lt;slug&gt;</c>, or a bare OpenRouter slug such as <c>openai/gpt-4o</c>.
/// </summary>
/// <param name="options">Base options; the OpenRouter endpoint uses them as they are, other endpoints replace address, dialect and key.</param>
/// <param name="catalog">The model list of the base endpoint.</param>
/// <param name="time">The clock.</param>
/// <param name="loggerFactory">Logging.</param>
/// <param name="endpoints">The configured endpoints besides OpenRouter.</param>
/// <param name="catalogFor">Creates the model list of another endpoint.</param>
/// <param name="maxOutputUsdPerMillion">OpenRouter models that charge more per million output tokens are refused; null for no limit.</param>
public sealed class PlayerFactory(
    EndpointOptions options,
    ModelCatalog catalog,
    TimeProvider time,
    ILoggerFactory? loggerFactory = null,
    EndpointRegistry? endpoints = null,
    Func<EndpointOptions, ModelCatalog>? catalogFor = null,
    decimal? maxOutputUsdPerMillion = null)
{
    /// <summary>Creates a player. <paramref name="seed"/> seeds the random player and the optimal player.</summary>
    public async Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        var trimmed = spec.Trim();
        return trimmed switch
        {
            "random" => new RandomPlayer(seed),
            "optimal" => new OptimalPlayer(seed),
            _ when trimmed.StartsWith("scripted:", StringComparison.Ordinal) => await ReadScriptAsync(trimmed["scripted:".Length..], cancellationToken),
            "scripted" => throw new PlayerSpecException("'scripted' needs a file: scripted:<file.json> (a JSON array of answer strings)."),
            _ => await CreateModelPlayerAsync(trimmed, cancellationToken),
        };
    }

    private (EndpointOptions Options, ModelCatalog Catalog, string Slug) ResolveEndpoint(string spec)
    {
        var (name, slug) = EndpointRegistry.Split(spec);
        if (name is null || string.Equals(name, EndpointRegistry.OpenRouterName, StringComparison.OrdinalIgnoreCase))
        {
            return (options, catalog, name is null ? spec : slug);
        }

        var registry = endpoints ?? new EndpointRegistry();
        var resolved = registry.Resolve(name, options) ?? throw new PlayerSpecException(
            $"Unknown endpoint '{name}' in '{spec}'. Configured endpoints: {string.Join(", ", registry.Names.Prepend(EndpointRegistry.OpenRouterName))}. " +
            "Endpoints are defined in shootout.settings.json, see docs/endpoints.md.");
        var resolvedCatalog = catalogFor?.Invoke(resolved) ?? ModelCatalog.ForEndpoint(resolved, time);
        return (resolved, resolvedCatalog, slug);
    }

    private static void RequireSupportedParameters(EndpointOptions endpoint, ModelInfo model)
    {
        if (endpoint.Kind != EndpointKind.OpenRouter)
        {
            return;
        }

        void Require(bool configured, string label, params string[] names)
        {
            if (configured && !names.Any(model.SupportedParameters.Contains))
            {
                throw new PlayerSpecException(
                    $"Model '{model.Id}' does not support '{label}', but it is configured. " +
                    "OpenRouter would reject the request when a JSON schema is sent. Remove the setting or choose another model.");
            }
        }

        Require(endpoint.Temperature is not null, "temperature", "temperature");
        Require(endpoint.ReasoningEffort is not null, "reasoning effort", "reasoning", "reasoning_effort");
        Require(endpoint.MaxOutputTokens is not null, "max output tokens", "max_tokens", "max_completion_tokens");
    }

    private static async Task<IPlayer> ReadScriptAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new PlayerSpecException($"Script file '{path}' does not exist.");
        }

        string[] answers;
        try
        {
            answers = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path, cancellationToken)) ?? [];
        }
        catch (JsonException ex)
        {
            throw new PlayerSpecException($"Script file '{path}' must be a JSON array of strings: {ex.Message}", ex);
        }

        var player = new ScriptedPlayer($"scripted:{Path.GetFileName(path)}");
        foreach (var answer in answers)
        {
            player.Reply(answer);
        }

        return player;
    }

    private async Task<IPlayer> CreateModelPlayerAsync(string spec, CancellationToken cancellationToken)
    {
        var (endpoint, models, slug) = ResolveEndpoint(spec);
        if (endpoint.Kind == EndpointKind.OpenRouter && string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            throw new PlayerSpecException(
                $"The environment variable {EndpointOptions.OpenRouterKeyVariable} is not set. " +
                "Create a key at https://openrouter.ai/keys, export it, or play with 'random' players.");
        }

        var model = await FindModelAsync(endpoint, models, slug, cancellationToken);
        RequireSupportedParameters(endpoint, model);
        if (endpoint.Kind == EndpointKind.OpenRouter && maxOutputUsdPerMillion is { } ceiling && model.CompletionUsdPerMillion > ceiling)
        {
            throw new PlayerSpecException($"Model '{model.Id}' costs {model.CompletionUsdPerMillion:0.##} USD per million output tokens, above the limit of {ceiling:0.##}. Raise Web__MaxOutputUsdPerMillion to allow it.");
        }

        var http = ChatClientFactory.CreateHttpClient(endpoint);
        var client = ChatClientFactory.CreateChatClient(endpoint, model.Id, http, loggerFactory);
        return new ChatPlayer(client, model.Id, endpoint, model.SupportsJsonSchema, time, endpoint.Kind == EndpointKind.OpenRouter ? model : null);
    }

    /// <summary>
    /// Looks the model up in the list of its endpoint. If another endpoint cannot deliver its list, the model is trusted:
    /// the first call reports an unknown model anyway.
    /// </summary>
    private static async Task<ModelInfo> FindModelAsync(EndpointOptions endpoint, ModelCatalog models, string slug, CancellationToken cancellationToken)
    {
        ModelInfo? model;
        try
        {
            model = await models.FindAsync(slug, cancellationToken);
        }
        catch (HttpRequestException) when (endpoint.Kind != EndpointKind.OpenRouter)
        {
            return new ModelInfo(slug, slug, 0m, 0m, 0, true, false, new HashSet<string>());
        }
        catch (HttpRequestException ex)
        {
            throw new PlayerSpecException($"Cannot check model '{slug}': the OpenRouter model list is not reachable ({ex.Message}).", ex);
        }

        return model ?? throw new PlayerSpecException(UnknownModelMessage(endpoint, slug));
    }

    private static string UnknownModelMessage(EndpointOptions endpoint, string slug) =>
        endpoint.Kind == EndpointKind.OpenRouter
            ? $"Model '{slug}' does not exist on OpenRouter. Find the exact slug with: shootout models --filter {slug.Split('/')[^1]}"
            : $"Model '{slug}' is not offered by endpoint '{endpoint.EndpointName}'. List its models with: shootout models --endpoint {endpoint.EndpointName}";
}
