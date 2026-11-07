using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Runs;

/// <summary>Creates players from their specification and lists the models that can be typed into the form.</summary>
internal sealed class PlayerSetup(IOptions<EndpointOptions> openRouter, EndpointRegistry endpoints, TimeProvider time, ILoggerFactory loggers, ILogger<PlayerSetup> logger, IOptions<WebOptions> web) : IPlayerSource
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(8);

    public Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken) =>
        new PlayerFactory(
            openRouter.Value,
            ModelCatalog.ForEndpoint(openRouter.Value, time),
            time,
            loggers,
            endpoints,
            options => ModelCatalog.ForEndpoint(options, time),
            web.Value.MaxOutputUsdPerMillion).CreateAsync(spec, seed, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SuggestionsAsync(CancellationToken cancellationToken)
    {
        var suggestions = new List<string> { "random", "optimal" };
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

    private async Task<IReadOnlyList<string>> ListAsync(EndpointOptions? options, CancellationToken cancellationToken)
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
