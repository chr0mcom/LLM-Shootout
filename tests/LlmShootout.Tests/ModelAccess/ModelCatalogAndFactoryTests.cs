using System.Net;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using Microsoft.Extensions.Time.Testing;

namespace LlmShootout.Tests.ModelAccess;

public sealed class ModelCatalogAndFactoryTests : IDisposable
{
    private const string Models = """
        {"data":[
          {"id":"vendor/alpha","name":"Alpha","context_length":128000,"pricing":{"prompt":"0.000001","completion":"0.000004"},"supported_parameters":["temperature","structured_outputs","reasoning"]},
          {"id":"vendor/beta","name":"Beta","context_length":8192,"pricing":{"prompt":"0","completion":"0"},"supported_parameters":["temperature"]},
          {"id":"vendor/gamma","name":"Gamma","context_length":4096,"pricing":{"prompt":"0","completion":"0"},"supported_parameters":["structured_outputs"]},
          {"name":"no id"}
        ]}
        """;

    private readonly string _cache = TempDirectory.Create("tests");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    private EndpointOptions Options(string? key = "k") => new() { ApiKey = key, CacheDirectory = _cache };

    private ModelCatalog Catalog(FakeEndpointHandler handler, EndpointOptions options) =>
        new(new HttpClient(handler) { BaseAddress = options.BaseAddress }, options, _time);

    [Fact]
    public void Parses_prices_per_million_and_capabilities()
    {
        var models = ModelCatalog.Parse(Models);

        Assert.Equal(3, models.Count);
        var alpha = models[0];
        Assert.Equal(("vendor/alpha", 1m, 4m, 128000), (alpha.Id, alpha.PromptUsdPerMillion, alpha.CompletionUsdPerMillion, alpha.ContextLength));
        Assert.True(alpha.SupportsJsonSchema);
        Assert.True(alpha.SupportsReasoning);
        Assert.False(models[1].SupportsJsonSchema);
    }

    [Fact]
    public async Task Uses_the_file_cache_within_its_lifetime_and_refreshes_after()
    {
        var options = Options();
        var handler = new FakeEndpointHandler().Ok(Models).Ok(Models);
        var catalog = Catalog(handler, options);

        await catalog.GetModelsAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));
        File.SetLastWriteTimeUtc(Path.Combine(_cache, "models.json"), _time.GetUtcNow().UtcDateTime);
        await catalog.GetModelsAsync(TestContext.Current.CancellationToken);
        Assert.Single(handler.Requests);

        _time.Advance(ModelCatalog.CacheLifetime + TimeSpan.FromMinutes(1));
        await catalog.GetModelsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_to_a_stale_cache_when_the_network_fails()
    {
        var options = Options();
        await Catalog(new FakeEndpointHandler().Ok(Models), options).GetModelsAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromDays(1));

        var offline = new FakeEndpointHandler().Respond(HttpStatusCode.ServiceUnavailable, "{}");
        var models = await Catalog(offline, options).GetModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, models.Count);
    }

    [Fact]
    public async Task Finds_models_case_insensitively()
    {
        var found = await Catalog(new FakeEndpointHandler().Ok(Models), Options()).FindAsync("Vendor/Alpha", TestContext.Current.CancellationToken);

        Assert.Equal("vendor/alpha", found!.Id);
    }

    private PlayerFactory Factory(EndpointOptions options, FakeEndpointHandler handler) => new(options, Catalog(handler, options), _time);

    [Fact]
    public async Task Creates_random_and_scripted_players()
    {
        var factory = Factory(Options(), new FakeEndpointHandler());
        var script = Path.Combine(_cache, "script.json");
        Directory.CreateDirectory(_cache);
        await File.WriteAllTextAsync(script, "[\"{}\",\"{}\"]", TestContext.Current.CancellationToken);

        Assert.Equal("random", (await factory.CreateAsync("random", 1, TestContext.Current.CancellationToken)).Name);
        var scripted = await factory.CreateAsync($"scripted:{script}", 1, TestContext.Current.CancellationToken);
        Assert.Equal("scripted:script.json", scripted.Name);
        var reply = await scripted.RespondAsync(new PlayerCall(CallPurpose.Turn, [], null, 1, _ => "x"), TestContext.Current.CancellationToken);
        Assert.Equal("{}", reply.Content);
    }

    [Theory]
    [InlineData("scripted", "needs a file")]
    [InlineData("scripted:/nonexistent/file.json", "does not exist")]
    public async Task Explains_script_mistakes(string spec, string expected)
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Options(), new FakeEndpointHandler()).CreateAsync(spec, 1, TestContext.Current.CancellationToken));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_malformed_script()
    {
        Directory.CreateDirectory(_cache);
        var path = Path.Combine(_cache, "bad.json");
        await File.WriteAllTextAsync(path, "{not an array}", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Options(), new FakeEndpointHandler()).CreateAsync($"scripted:{path}", 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_missing_api_key_gets_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Options(key: null), new FakeEndpointHandler()).CreateAsync("vendor/alpha", 1, TestContext.Current.CancellationToken));

        Assert.Contains("OPENROUTER_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_model_gets_a_clear_message_with_a_hint()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Options(), new FakeEndpointHandler().Ok(Models)).CreateAsync("vendor/delta", 1, TestContext.Current.CancellationToken));

        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
        Assert.Contains("shootout models --filter delta", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_model_list_gets_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Options(), new FakeEndpointHandler().Respond(HttpStatusCode.ServiceUnavailable, "{}")).CreateAsync("vendor/alpha", 1, TestContext.Current.CancellationToken));

        Assert.Contains("not reachable", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vendor/alpha")]
    [InlineData("openrouter:vendor/alpha")]
    public async Task Creates_a_chat_player_from_a_slug_with_or_without_prefix(string spec)
    {
        var player = await Factory(Options(), new FakeEndpointHandler().Ok(Models)).CreateAsync(spec, 1, TestContext.Current.CancellationToken);

        Assert.Equal("openrouter:vendor/alpha", player.Name);
        Assert.Equal("vendor/alpha", player.Model!.Slug);
    }

    [Fact]
    public void The_options_text_never_contains_the_api_key()
    {
        var text = new EndpointOptions { ApiKey = "sk-or-v1-SECRET" }.ToString();

        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        Assert.Contains("<set>", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_parameters_the_model_does_not_support_are_refused_at_creation()
    {
        var options = Options() with { Temperature = 0.3f };
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(options, new FakeEndpointHandler().Ok(Models)).CreateAsync("vendor/gamma", 1, TestContext.Current.CancellationToken));

        Assert.Contains("does not support 'temperature'", ex.Message, StringComparison.Ordinal);
        var effort = Options() with { ReasoningEffort = "low" };
        var ex2 = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(effort, new FakeEndpointHandler().Ok(Models)).CreateAsync("vendor/beta", 1, TestContext.Current.CancellationToken));
        Assert.Contains("reasoning effort", ex2.Message, StringComparison.Ordinal);
        var ok = await Factory(effort, new FakeEndpointHandler().Ok(Models)).CreateAsync("vendor/alpha", 1, TestContext.Current.CancellationToken);
        Assert.Equal("vendor/alpha", ok.Model!.Slug);
    }

    [Fact]
    public void Takes_the_api_key_from_the_environment()
    {
        var options = new EndpointOptions { ApiKey = Environment.GetEnvironmentVariable(EndpointOptions.OpenRouterKeyVariable) };

        Assert.Equal(Environment.GetEnvironmentVariable(EndpointOptions.OpenRouterKeyVariable), options.ApiKey);
    }
}
