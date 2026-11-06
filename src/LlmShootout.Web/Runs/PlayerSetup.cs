using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Runs;

/// <summary>Creates players from their specification and lists the models that can be typed into the form.</summary>
internal class PlayerSetup(IOptions<OpenRouterOptions> openRouter, EndpointRegistry endpoints, TimeProvider time, ILoggerFactory loggers, ILogger<PlayerSetup> logger, IOptions<WebOptions>? web = null)
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(8);

    public IReadOnlyCollection<string> EndpointNames => [EndpointRegistry.OpenRouterName, .. endpoints.Names];

    public virtual Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken) =>
        new PlayerFactory(
            openRouter.Value,
            ModelCatalog.ForEndpoint(openRouter.Value, time),
            time,
            loggers,
            endpoints,
            options => ModelCatalog.ForEndpoint(options, time),
            web?.Value.MaxOutputUsdPerMillion).CreateAsync(spec, seed, cancellationToken);

    /// <summary>Player specifications for the form: <c>random</c>, every model of every configured endpoint, and OpenRouter slugs if a key is set.</summary>
    public async Task<IReadOnlyList<string>> SuggestionsAsync(CancellationToken cancellationToken)
    {
        var suggestions = new List<string> { "random" };
        foreach (var name in endpoints.Names)
        {
            suggestions.AddRange((await ListAsync(endpoints.Resolve(name, openRouter.Value), cancellationToken)).Select(id => $"{name}:{id}"));
        }

        if (!string.IsNullOrWhiteSpace(openRouter.Value.ApiKey))
        {
            suggestions.AddRange(await ListAsync(openRouter.Value, cancellationToken));
        }

        return suggestions;
    }

    private async Task<IReadOnlyList<string>> ListAsync(OpenRouterOptions? options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListTimeout);
        try
        {
            var models = await ModelCatalog.ForEndpoint(options, time).GetModelsAsync(timeout.Token);
            return [.. models.Select(m => m.Id).Where(id => !id.EndsWith(":batch", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                WebLog.ModelListUnavailable(logger, options.EndpointName, ex.Message);
            }

            return [];
        }
    }
}
