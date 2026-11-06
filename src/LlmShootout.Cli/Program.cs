using System.CommandLine;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Cli;
using LlmShootout.Cli.Commands;
using LlmShootout.Cli.Output;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder([]);
builder.Configuration.AddEnvironmentVariables("LLMSHOOTOUT_");
if (Environment.GetEnvironmentVariable(OpenRouterOptions.ApiKeyVariable) is { Length: > 0 } apiKey)
{
    builder.Configuration["OpenRouter:ApiKey"] = apiKey;
}

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.SetMinimumLevel(Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("LLMSHOOTOUT_LOG"), true, out var level) ? level : LogLevel.Warning);
builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => ConsoleFactory.Create(Console.Out));
builder.Services.AddOptions<OpenRouterOptions>().Bind(builder.Configuration.GetSection("OpenRouter")).ValidateDataAnnotations();
builder.Services.AddSingleton<CliEnvironment>();

using var host = builder.Build();
var env = host.Services.GetRequiredService<CliEnvironment>();

var root = new RootCommand("LLM Shootout: two language models play a game against each other. Start with: shootout play split-or-steal --a random --b random")
{
    PlayCommand.Build(env),
    SeriesCommand.Build(env),
    InfoCommands.BuildGames(env),
    InfoCommands.BuildModels(env),
    ReplayCommand.Build(env),
    DerivedCommands.BuildReport(env),
    DerivedCommands.BuildDebrief(env),
    DerivedCommands.BuildAnnotate(env),
};

var parse = root.Parse(args);
if (parse.Errors.Count > 0)
{
    foreach (var error in parse.Errors)
    {
        env.Error.MarkupLine($"[red]Error:[/] {Markup.Escape(error.Message)}");
    }

    env.Error.WriteLine("Try: shootout --help");
    return ErrorHandling.UsageError;
}

return await parse.InvokeAsync();
