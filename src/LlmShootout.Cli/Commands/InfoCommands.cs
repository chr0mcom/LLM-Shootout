using System.CommandLine;
using System.Globalization;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Templates;
using LlmShootout.Games;
using Spectre.Console;

namespace LlmShootout.Cli.Commands;

/// <summary>shootout games and shootout models.</summary>
public static class InfoCommands
{
    /// <summary>Creates the <c>games</c> command.</summary>
    public static Command BuildGames(CliEnvironment env)
    {
        var command = new Command("games", "Lists the available games.");
        command.SetAction(_ =>
        {
            var table = new Table().AddColumns("Id", "Name", "Summary");
            foreach (var game in GameCatalog.All)
            {
                table.AddRow(Markup.Escape(game.Id), Markup.Escape(game.DisplayName), Markup.Escape(game.Summary));
            }

            env.Console.Write(table);
            env.Console.WriteLine("Prompt languages: " + string.Join(", ", ArenaTemplates.Languages));
            return 0;
        });
        return command;
    }

    /// <summary>Creates the <c>models</c> command.</summary>
    public static Command BuildModels(CliEnvironment env)
    {
        var filter = new Option<string?>("--filter") { Description = "Only models whose id or name contains this text." };
        var schemaOnly = new Option<bool>("--schema-only") { Description = "Only models that support JSON schema output." };
        var limit = new Option<int>("--limit") { Description = "Show at most this many models.", DefaultValueFactory = _ => 40 };
        var from = new Option<string?>("--from") { Description = "Read a saved response of the models endpoint instead of calling OpenRouter." };
        var endpoint = new Option<string?>("--endpoint") { Description = "Name of a configured endpoint (default: openrouter). See docs/endpoints.md." };
        var command = new Command("models", "Lists the models of OpenRouter (with prices) or of a configured endpoint. OpenRouter needs no API key.") { filter, schemaOnly, limit, from, endpoint };

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var options = ResolveEndpoint(env, parse.GetValue(endpoint));
            var models = parse.GetValue(from) is { } path
                ? ModelCatalog.Parse(await File.ReadAllTextAsync(path, ct), options.Kind)
                : await env.CreateCatalog(options).GetModelsAsync(ct);
            var text = parse.GetValue(filter);
            var selected = models
                .Where(m => text is null || m.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Where(m => !parse.GetValue(schemaOnly) || m.SupportsJsonSchema)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToArray();

            var table = new Table().AddColumns("Model", "Input $/M", "Output $/M", "Context", "JSON schema", "Reasoning");
            foreach (var m in selected.Take(parse.GetValue(limit)))
            {
                table.AddRow(
                    Markup.Escape(m.Id),
                    options.Kind == EndpointKind.OpenRouter ? m.PromptUsdPerMillion.ToString("0.##", CultureInfo.InvariantCulture) : "-",
                    options.Kind == EndpointKind.OpenRouter ? m.CompletionUsdPerMillion.ToString("0.##", CultureInfo.InvariantCulture) : "-",
                    m.ContextLength.ToString(CultureInfo.InvariantCulture),
                    options.Kind == EndpointKind.OpenRouter ? (m.SupportsJsonSchema ? "yes" : "no") : "-",
                    options.Kind == EndpointKind.OpenRouter ? (m.SupportsReasoning ? "yes" : "no") : "-");
            }

            env.Console.Write(table);
            env.Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Math.Min(selected.Length, parse.GetValue(limit))} of {selected.Length} matching models shown ({models.Count} in total)."));
            return 0;
        }, ct));
        return command;
    }

    private static EndpointOptions ResolveEndpoint(CliEnvironment env, string? name) =>
        name is null
            ? env.BaseOptions
            : env.Endpoints.Resolve(name, env.BaseOptions)
                ?? throw new UsageException($"Unknown endpoint '{name}'. Configured endpoints: {string.Join(", ", env.Endpoints.Names.Prepend(EndpointRegistry.OpenRouterName))}. See docs/endpoints.md.");
}
