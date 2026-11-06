using System.ComponentModel.DataAnnotations;
using LlmShootout.Arena;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Players;
using LlmShootout.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace LlmShootout.Cli;

/// <summary>Everything the commands share: console, clock, the validated base options, and helpers that create players and catalogs.</summary>
public sealed class CliEnvironment(IAnsiConsole console, TimeProvider time, IOptions<OpenRouterOptions> openRouter, ILoggerFactory loggerFactory)
{
    /// <summary>Output for people.</summary>
    public IAnsiConsole Console => console;

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

    /// <summary>Creates a catalog of OpenRouter models.</summary>
    public ModelCatalog CreateCatalog(OpenRouterOptions options) =>
        new(new HttpClient { BaseAddress = options.BaseAddress, Timeout = TimeSpan.FromSeconds(30) }, options, time);

    /// <summary>Creates a player from its specification.</summary>
    public Task<IPlayer> CreatePlayerAsync(string spec, long seed, OpenRouterOptions options, CancellationToken cancellationToken) =>
        new PlayerFactory(options, CreateCatalog(options), time, loggerFactory).CreateAsync(spec, seed, cancellationToken);

    /// <summary>Creates a match runner that reports every protocol event to <paramref name="observer"/>.</summary>
    public MatchRunner CreateRunner(CostMeter cost, Action<Arena.Protocol.MatchEvent>? observer) =>
        new(cost, time, loggerFactory.CreateLogger<MatchRunner>(), observer);
}
