using System.Text.Json;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.Logging;

namespace LlmShootout.Arena.OpenRouter;

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
/// Turns a player specification into a player: <c>random</c>, <c>scripted:&lt;file&gt;</c>,
/// <c>openrouter:&lt;slug&gt;</c>, or a bare slug such as <c>openai/gpt-4o</c>.
/// </summary>
public sealed class PlayerFactory(OpenRouterOptions options, ModelCatalog catalog, TimeProvider time, ILoggerFactory? loggerFactory = null)
{
    /// <summary>Creates a player. <paramref name="seed"/> seeds the random player.</summary>
    public async Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        var trimmed = spec.Trim();
        return trimmed switch
        {
            "random" => new RandomPlayer(seed),
            _ when trimmed.StartsWith("scripted:", StringComparison.Ordinal) => await ReadScriptAsync(trimmed["scripted:".Length..], cancellationToken),
            "scripted" => throw new PlayerSpecException("'scripted' needs a file: scripted:<file.json> (a JSON array of answer strings)."),
            _ => await CreateModelPlayerAsync(trimmed.StartsWith("openrouter:", StringComparison.Ordinal) ? trimmed["openrouter:".Length..] : trimmed, cancellationToken),
        };
    }

    private void RequireSupportedParameters(ModelInfo model)
    {
        void Require(bool configured, string label, params string[] names)
        {
            if (configured && !names.Any(model.SupportedParameters.Contains))
            {
                throw new PlayerSpecException(
                    $"Model '{model.Id}' does not support '{label}', but it is configured. " +
                    "OpenRouter would reject the request when a JSON schema is sent. Remove the setting or choose another model.");
            }
        }

        Require(options.Temperature is not null, "temperature", "temperature");
        Require(options.ReasoningEffort is not null, "reasoning effort", "reasoning", "reasoning_effort");
        Require(options.MaxOutputTokens is not null, "max output tokens", "max_tokens", "max_completion_tokens");
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

    private async Task<IPlayer> CreateModelPlayerAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new PlayerSpecException(
                $"The environment variable {OpenRouterOptions.ApiKeyVariable} is not set. " +
                "Create a key at https://openrouter.ai/keys, export it, or play with 'random' players.");
        }

        ModelInfo? model;
        try
        {
            model = await catalog.FindAsync(slug, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PlayerSpecException($"Cannot check model '{slug}': the OpenRouter model list is not reachable ({ex.Message}).", ex);
        }

        if (model is null)
        {
            throw new PlayerSpecException($"Model '{slug}' does not exist on OpenRouter. Find the exact slug with: shootout models --filter {slug.Split('/')[^1]}");
        }

        RequireSupportedParameters(model);
        var http = OpenRouterClientFactory.CreateHttpClient(options);
        var client = OpenRouterClientFactory.CreateChatClient(options, model.Id, http, loggerFactory);
        return new ChatPlayer(client, model.Id, options, model.SupportsJsonSchema, time, model);
    }
}
