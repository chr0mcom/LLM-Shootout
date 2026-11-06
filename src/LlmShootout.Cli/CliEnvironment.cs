using System.ComponentModel.DataAnnotations;
using LlmShootout.Arena;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Players;
using LlmShootout.Cli.Output;
using LlmShootout.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace LlmShootout.Cli;

/// <summary>Everything the commands share: console, clock, the validated base options, and helpers that create players and catalogs.</summary>
public sealed class CliEnvironment(IAnsiConsole console, TimeProvider time, IOptions<OpenRouterOptions> openRouter, ILoggerFactory loggerFactory, EndpointRegistry endpoints)
{
    private readonly Lazy<IAnsiConsole> _error = new(() => ConsoleFactory.Create(System.Console.Error));

    /// <summary>Output for people.</summary>
    public IAnsiConsole Console => console;

    /// <summary>Output for errors, on standard error.</summary>
    public IAnsiConsole Error => _error.Value;

    /// <summary>The clock.</summary>
    public TimeProvider Time => time;

    /// <summary>Logging.</summary>
    public ILoggerFactory LoggerFactory => loggerFactory;

    /// <summary>The OpenRouter options from configuration (already validated at startup).</summary>
    public OpenRouterOptions BaseOptions => openRouter.Value;

    /// <summary>Finds a game or explains which games exist.</summary>
    public static Arena.Games.IGameDefinition RequireGame(string id) =>
        GameCatalog.Find(id) ?? throw new UsageException($"Unknown game '{id}'. Available games: {string.Join(", ", GameCatalog.All.Select(g => g.Id))}. See: shootout games");

    /// <summary>Validates options after command line overrides were applied.</summary>
    public static OpenRouterOptions Validate(OpenRouterOptions options)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            throw new UsageException("Invalid settings: " + string.Join("; ", results.Select(r => r.ErrorMessage)));
        }

        return options;
    }

    /// <summary>The configured endpoints besides OpenRouter.</summary>
    public EndpointRegistry Endpoints => endpoints;

    /// <summary>Creates the model list of an endpoint.</summary>
    public ModelCatalog CreateCatalog(OpenRouterOptions options) => ModelCatalog.ForEndpoint(options, time);

    /// <summary>Creates a player from its specification.</summary>
    public Task<IPlayer> CreatePlayerAsync(string spec, long seed, OpenRouterOptions options, CancellationToken cancellationToken) =>
        new PlayerFactory(options, CreateCatalog(options), time, loggerFactory, endpoints, CreateCatalog).CreateAsync(spec, seed, cancellationToken);

    /// <summary>Creates a match runner that reports every protocol event to <paramref name="observer"/>.</summary>
    public MatchRunner CreateRunner(CostMeter cost, Action<Arena.Protocol.MatchEvent>? observer) =>
        new(cost, time, loggerFactory.CreateLogger<MatchRunner>(), observer);
}
