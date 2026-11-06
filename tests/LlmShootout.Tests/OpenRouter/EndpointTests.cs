using System.Net;
using System.Text.Json.Nodes;
using LlmShootout.Arena.OpenRouter;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Series;

namespace LlmShootout.Tests.OpenRouter;

public sealed class EndpointTests : IDisposable
{
    private const string ProxyModels = """{"object":"list","data":[{"id":"claude-sonnet-latest","object":"model","owned_by":"anthropic"},{"id":"claude-opus-5-5"}]}""";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "llm-shootout-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    private static EndpointRegistry Registry(Dictionary<string, string?>? environment = null, bool flatRate = true) =>
        new(
            new Dictionary<string, EndpointDefinition>
            {
                ["claude"] = new() { BaseAddress = new Uri("http://127.0.0.1:8090/v1/claude"), FlatRate = flatRate, ApiKeyVariable = "PROXY_KEY" },
                ["codex"] = new() { BaseAddress = new Uri("http://127.0.0.1:8090/v1/codex/") },
                ["broken"] = new() { BaseAddress = null },
            },
            name => environment?.GetValueOrDefault(name));

    private OpenRouterOptions Base() => new() { ApiKey = "sk-or-OPENROUTER", CacheDirectory = _cache };

    [Theory]
    [InlineData("claude:claude-sonnet-latest", "claude", "claude-sonnet-latest")]
    [InlineData("openrouter:vendor/alpha", "openrouter", "vendor/alpha")]
    [InlineData("codex:gpt-6.1-sol:batch", "codex", "gpt-6.1-sol:batch")]
    [InlineData("vendor/alpha", null, "vendor/alpha")]
    [InlineData("deepseek/deepseek-r1:free", null, "deepseek/deepseek-r1:free")]
    public void Splits_a_specification_into_endpoint_and_model(string spec, string? endpoint, string model) =>
        Assert.Equal((endpoint, model), EndpointRegistry.Split(spec));

    [Fact]
    public void Resolves_address_dialect_flat_rate_and_key_of_an_endpoint()
    {
        var options = Registry(new() { ["PROXY_KEY"] = "secret-proxy" }).Resolve("Claude", Base())!;

        Assert.Equal(("claude", EndpointKind.OpenAiCompatible, true), (options.EndpointName, options.Kind, options.FlatRate));
        Assert.Equal("http://127.0.0.1:8090/v1/claude/", options.BaseAddress.AbsoluteUri);
        Assert.Equal("secret-proxy", options.ApiKey);
        Assert.Equal("PROXY_KEY", options.ApiKeyVariableName);
        Assert.DoesNotContain("secret-proxy", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_key_from_a_variable_named_after_the_endpoint_when_none_is_configured()
    {
        var options = Registry(new() { ["LLMSHOOTOUT_CODEX_API_KEY"] = "k2" }).Resolve("codex", Base())!;

        Assert.Equal("k2", options.ApiKey);
        Assert.False(options.FlatRate);
    }

    [Fact]
    public void An_endpoint_without_a_key_is_allowed_and_openrouter_keeps_the_base_options()
    {
        var registry = Registry();

        Assert.Null(registry.Resolve("codex", Base())!.ApiKey);
        Assert.Same(Base().BaseAddress.AbsoluteUri, registry.Resolve("openrouter", Base())!.BaseAddress.AbsoluteUri);
        Assert.Equal("sk-or-OPENROUTER", registry.Resolve("openrouter", Base())!.ApiKey);
        Assert.Null(registry.Resolve("nobody", Base()));
    }

    [Fact]
    public void An_endpoint_without_an_address_is_refused_with_an_example()
    {
        var ex = Assert.Throws<PlayerSpecException>(() => Registry().Resolve("broken", Base()));

        Assert.Contains("BaseAddress", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude", "LLMSHOOTOUT_CLAUDE_API_KEY")]
    [InlineData("my-proxy.1", "LLMSHOOTOUT_MY_PROXY_1_API_KEY")]
    public void The_default_key_variable_is_derived_from_the_name(string name, string variable) =>
        Assert.Equal(variable, EndpointRegistry.DefaultKeyVariable(name));

    [Fact]
    public async Task Other_endpoints_get_no_openrouter_fields_and_a_top_level_reasoning_effort()
    {
        var options = Registry(new() { ["PROXY_KEY"] = "pk" }).Resolve("claude", Base())! with { ReasoningEffort = "low", RetryDelay = TimeSpan.FromMilliseconds(1) };
        var handler = new FakeOpenRouterHandler().Ok(FakeOpenRouterHandler.Completion("{}", cost: null));
        var player = CreatePlayer(handler, options, schema: true);

        await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], JsonNode.Parse("""{"type":"object"}"""), 1, _ => "{}"), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Null(body["provider"]);
        Assert.Null(body["reasoning"]);
        Assert.Equal("low", body["reasoning_effort"]!.ToString());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.False(handler.Messages[0].Headers.Contains("X-Title"));
        Assert.Equal("Bearer pk", handler.Messages[0].Headers.GetValues("Authorization").Single());
        Assert.Equal("http://127.0.0.1:8090/v1/claude/chat/completions", handler.Messages[0].RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, "0")]
    [InlineData(false, null)]
    public async Task Without_a_reported_cost_a_flat_rate_call_counts_as_zero_and_any_other_stays_unknown(bool flatRate, string? expected)
    {
        var options = Registry(flatRate: flatRate).Resolve("claude", Base())!;
        var player = CreatePlayer(new FakeOpenRouterHandler().Ok(FakeOpenRouterHandler.Completion("{}", cost: null)), options);

        var reply = await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken);

        Assert.Null(reply.Call!.CostUsd);
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), reply.Call.EstimatedCostUsd);
        Assert.Equal("claude:vendor/model", player.Name);
    }

    [Fact]
    public async Task A_failure_names_the_endpoint_not_openrouter()
    {
        var options = Registry().Resolve("claude", Base())! with { MaxHttpRetries = 0 };
        var player = CreatePlayer(new FakeOpenRouterHandler().Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}"""), options);

        var ex = await Assert.ThrowsAsync<PlayerFailureException>(() => player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken));

        Assert.Contains("endpoint 'claude' returned HTTP 401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_list_of_another_endpoint_has_only_ids_and_counts_as_schema_capable()
    {
        var models = ModelCatalog.Parse(ProxyModels, EndpointKind.OpenAiCompatible);

        Assert.Equal(["claude-sonnet-latest", "claude-opus-5-5"], models.Select(m => m.Id));
        Assert.All(models, m => Assert.True(m.SupportsJsonSchema));
        Assert.All(models, m => Assert.Equal(0m, m.PromptUsdPerMillion));
    }

    [Fact]
    public async Task Every_endpoint_has_its_own_model_list_cache()
    {
        var claude = Registry().Resolve("claude", Base())!;
        var codex = Registry().Resolve("codex", Base())!;
        var token = TestContext.Current.CancellationToken;

        await Catalog(new FakeOpenRouterHandler().Ok(ProxyModels), claude).GetModelsAsync(token);
        await Catalog(new FakeOpenRouterHandler().Ok("""{"data":[{"id":"gpt-6.1-sol"}]}"""), codex).GetModelsAsync(token);

        Assert.Equal(["claude-sonnet-latest", "claude-opus-5-5"], (await Catalog(new FakeOpenRouterHandler(), claude).GetModelsAsync(token)).Select(m => m.Id));
        Assert.Equal(["gpt-6.1-sol"], (await Catalog(new FakeOpenRouterHandler(), codex).GetModelsAsync(token)).Select(m => m.Id));
        Assert.True(File.Exists(Path.Combine(_cache, "models.claude.json")));
    }

    [Fact]
    public async Task The_factory_creates_a_player_for_an_endpoint_without_an_openrouter_key()
    {
        var factory = Factory(Base() with { ApiKey = null }, new FakeOpenRouterHandler().Ok(ProxyModels));

        var player = await factory.CreateAsync("claude:claude-sonnet-latest", 1, TestContext.Current.CancellationToken);

        Assert.Equal("claude:claude-sonnet-latest", player.Name);
        Assert.Equal("claude-sonnet-latest", player.Model!.Slug);
    }

    [Fact]
    public async Task Settings_that_openrouter_would_check_are_not_checked_against_a_list_without_parameters()
    {
        var factory = Factory(Base() with { Temperature = 0.4f }, new FakeOpenRouterHandler().Ok(ProxyModels));

        var player = await factory.CreateAsync("claude:claude-sonnet-latest", 1, TestContext.Current.CancellationToken);

        Assert.Equal("0.4", player.Settings["temperature"]);
    }

    [Fact]
    public async Task An_unknown_endpoint_lists_the_configured_ones()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Base(), new FakeOpenRouterHandler()).CreateAsync("gemini:x", 1, TestContext.Current.CancellationToken));

        Assert.Contains("Unknown endpoint 'gemini'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("openrouter, claude, codex, broken", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_model_points_to_the_model_list_of_the_endpoint()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Base(), new FakeOpenRouterHandler().Ok(ProxyModels)).CreateAsync("claude:gpt-9", 1, TestContext.Current.CancellationToken));

        Assert.Contains("shootout models --endpoint claude", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_whose_list_is_unreachable_is_trusted()
    {
        var factory = Factory(Base(), new FakeOpenRouterHandler().Respond(HttpStatusCode.BadGateway, "{}"));

        var player = await factory.CreateAsync("claude:anything", 1, TestContext.Current.CancellationToken);

        Assert.Equal("claude:anything", player.Name);
    }

    [Theory]
    [InlineData("claude:claude-sonnet-latest", "claude-sonnet-latest")]
    [InlineData("openrouter:openai/gpt-4o", "gpt-4o")]
    [InlineData("random", "random")]
    [InlineData("scripted:/tmp/a.json", "a.json")]
    public void File_names_do_not_contain_the_endpoint(string player, string slug) =>
        Assert.Equal(slug, MatchPaths.Slug(player));

    private static ModelCatalog Catalog(FakeOpenRouterHandler handler, OpenRouterOptions options) =>
        new(new HttpClient(handler) { BaseAddress = options.BaseAddress }, options, TimeProvider.System);

    private static PlayerFactory Factory(OpenRouterOptions options, FakeOpenRouterHandler listHandler) =>
        new(options, Catalog(new FakeOpenRouterHandler(), options), TimeProvider.System, null, Registry(), endpoint => Catalog(listHandler, endpoint));

    private static ChatPlayer CreatePlayer(FakeOpenRouterHandler handler, OpenRouterOptions options, bool schema = false)
    {
        var http = OpenRouterClientFactory.CreateHttpClient(options, handler);
        return new ChatPlayer(OpenRouterClientFactory.CreateChatClient(options, "vendor/model", http), "vendor/model", options, schema, TimeProvider.System);
    }
}
