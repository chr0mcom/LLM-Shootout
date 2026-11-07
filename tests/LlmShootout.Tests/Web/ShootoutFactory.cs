using LlmShootout.Arena.Identity;
using LlmShootout.Arena.Players;
using LlmShootout.Web.Runs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmShootout.Tests.Web;

internal sealed class ShootoutFactory(bool blockingPlayers = false, int loginAttempts = 5, string password = ShootoutFactory.Password, int maxMatchesPerDay = 30, string? root = null) : WebApplicationFactory<LlmShootout.Web.WebAssemblyMarker>
{
    public const string Password = "correct-horse-battery";

    public string Root { get; } = root ?? TempDirectory.Create("web");

    public string Matches => Path.Combine(Root, "matches");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Web:Password", password);
        builder.UseSetting("Web:MatchesDirectory", Matches);
        builder.UseSetting("Web:DataDirectory", Path.Combine(Root, "data"));
        builder.UseSetting("Web:MaxMatchesPerDay", maxMatchesPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Web:LoginAttemptsPerMinute", loginAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            if (blockingPlayers)
            {
                services.RemoveAll<IPlayerSource>();
                services.AddSingleton<IPlayerSource, HangingPlayerSource>();
            }
        });
    }

    public HttpClient NewClient(bool signedIn = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        if (signedIn)
        {
            using var response = client.PostAsync("/login", new FormUrlEncodedContent([new("password", password)])).GetAwaiter().GetResult();
            Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }

    private sealed class HangingPlayerSource : IPlayerSource
    {
        public Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken) =>
            Task.FromResult<IPlayer>(new HangingPlayer(spec));

        public Task<IReadOnlyList<string>> SuggestionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["slow"]);
    }

    private sealed class HangingPlayer(string name) : IPlayer
    {
        public string Name => name;

        public ModelIdentity? Model => null;

        public async Task<PlayerReply> RespondAsync(PlayerCall request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
