using LlmShootout.Arena.ModelAccess;
using LlmShootout.Web;
using LlmShootout.Web.Auth;
using LlmShootout.Web.Matches;
using LlmShootout.Web.Runs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shootout.settings.json", optional: true);
builder.Configuration.AddEnvironmentVariables("LLMSHOOTOUT_");
MapPlainVariable("SHOOTOUT_WEB_PASSWORD", "Web:Password");
MapPlainVariable(EndpointOptions.OpenRouterKeyVariable, "OpenRouter:ApiKey");

builder.Services.AddOptions<WebOptions>().Bind(builder.Configuration.GetSection("Web")).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EndpointOptions>().Bind(builder.Configuration.GetSection("OpenRouter")).ValidateDataAnnotations();
builder.Services.AddSingleton(_ => new EndpointRegistry(builder.Configuration.GetSection("Endpoints").Get<Dictionary<string, EndpointDefinition>>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PlayerSetup>();
builder.Services.AddSingleton<UsageLedger>();
builder.Services.AddSingleton<RunManager>();
builder.Services.AddHostedService(services => services.GetRequiredService<RunManager>());
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddSingleton<MatchArchive>();
builder.Services.AddPasswordGate(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});
builder.Services.AddDataProtection().SetApplicationName("llm-shootout");
builder.Services.AddOptions<KeyManagementOptions>().Configure<IOptions<WebOptions>>((keys, web) =>
    keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(web.Value.DataDirectory, "keys")), NullLoggerFactory.Instance));

var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseAuthentication();
app.UseGate();
app.UseAuthorization();
app.UseRateLimiter();
app.UseSameOriginOnly();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapShootout();
app.Run();

void MapPlainVariable(string variable, string key)
{
    if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
    {
        builder.Configuration[key] = value;
    }
}

namespace LlmShootout.Web
{
    /// <summary>Names this assembly for tests that start the application in memory.</summary>
    internal sealed class WebAssemblyMarker;
}
