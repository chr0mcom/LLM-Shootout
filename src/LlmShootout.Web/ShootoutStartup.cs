using LlmShootout.Arena.ModelAccess;
using LlmShootout.Web.Auth;
using LlmShootout.Web.Matches;
using LlmShootout.Web.Runs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web;

/// <summary>Configuration, services and request pipeline of the web interface, so that <c>Program.cs</c> reads in six lines.</summary>
internal static class ShootoutStartup
{
    private const int MaxRequestBodyBytes = 64 * 1024;

    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    /// <summary>Reads <c>shootout.settings.json</c>, <c>LLMSHOOTOUT_*</c> variables, and the two plain variables for the password and the OpenRouter key.</summary>
    public static void AddShootoutConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("shootout.settings.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("LLMSHOOTOUT_");
        MapVariable(builder.Configuration, "SHOOTOUT_WEB_PASSWORD", "Web:Password");
        MapVariable(builder.Configuration, EndpointOptions.OpenRouterKeyVariable, "OpenRouter:ApiKey");
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
    }

    public static void AddShootout(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WebOptions>().Bind(configuration.GetSection("Web")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EndpointOptions>().Bind(configuration.GetSection("OpenRouter")).ValidateDataAnnotations();
        services.AddSingleton(_ => new EndpointRegistry(configuration.GetSection("Endpoints").Get<Dictionary<string, EndpointDefinition>>()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPlayerSource, PlayerSetup>();
        services.AddSingleton<UsageLedger>();
        services.AddSingleton<RunManager>();
        services.AddHostedService(provider => provider.GetRequiredService<RunManager>());
        services.AddSingleton<MatchArchive>();
        services.AddPasswordGate();
        services.Configure<ForwardedHeadersOptions>(ForwardEverythingFromTheProxy);
        services.AddDataProtection().SetApplicationName("llm-shootout");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<WebOptions>>((keys, web) =>
            keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(web.Value.DataDirectory, "keys")), NullLoggerFactory.Instance));
    }

    /// <summary>The order matters: the gate must run before static files are served.</summary>
    public static void UseShootout(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(AddSecurityHeaders);
        app.UseAuthentication();
        app.UseGate();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseSameOriginOnly();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapShootout();
    }

    private static Task AddSecurityHeaders(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return next(context);
    }

    /// <summary>Only Traefik can reach the container (no published port), so its forwarding headers are believed.</summary>
    private static void ForwardEverythingFromTheProxy(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
    }

    private static void MapVariable(ConfigurationManager configuration, string variable, string key)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
        {
            configuration[key] = value;
        }
    }
}
