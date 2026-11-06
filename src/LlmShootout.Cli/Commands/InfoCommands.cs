using System.CommandLine;
using System.Globalization;
using LlmShootout.Arena.OpenRouter;
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
            var table = new Table().AddColumns("Id", "Name", "Question");
            foreach (var game in GameCatalog.All)
            {
                table.AddRow(Markup.Escape(game.Id), Markup.Escape(game.DisplayName), Markup.Escape(game.Question));
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
        var command = new Command("models", "Lists OpenRouter models with prices and JSON schema support. Needs no API key.") { filter, schemaOnly, limit, from };

        command.SetAction((parse, ct) => ErrorHandling.RunAsync(env.Error, async () =>
        {
            var models = parse.GetValue(from) is { } path
                ? ModelCatalog.Parse(await File.ReadAllTextAsync(path, ct))
                : await env.CreateCatalog(env.BaseOptions).GetModelsAsync(ct);
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
                    m.PromptUsdPerMillion.ToString("0.##", CultureInfo.InvariantCulture),
                    m.CompletionUsdPerMillion.ToString("0.##", CultureInfo.InvariantCulture),
                    m.ContextLength.ToString(CultureInfo.InvariantCulture),
                    m.SupportsJsonSchema ? "yes" : "no",
                    m.SupportsReasoning ? "yes" : "no");
            }

            env.Console.Write(table);
            env.Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Math.Min(selected.Length, parse.GetValue(limit))} of {selected.Length} matching models shown ({models.Count} in total)."));
            return 0;
        }));
        return command;
    }
}
