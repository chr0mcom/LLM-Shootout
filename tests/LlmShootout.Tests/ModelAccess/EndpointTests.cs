using System.Net;
using System.Text.Json.Nodes;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Series;

namespace LlmShootout.Tests.ModelAccess;

public sealed class EndpointTests : IDisposable
{
    private const string ServerModels = """{"object":"list","data":[{"id":"llama3.2","object":"model","owned_by":"library"},{"id":"llama3.3"}]}""";

    private readonly string _cache = TempDirectory.Create("tests");

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
                ["ollama"] = new() { BaseAddress = new Uri("http://localhost:11434/v1"), FlatRate = flatRate, ApiKeyVariable = "SERVER_KEY" },
                ["lmstudio"] = new() { BaseAddress = new Uri("http://localhost:1234/v1/") },
                ["broken"] = new() { BaseAddress = null },
            },
            name => environment?.GetValueOrDefault(name));

    private EndpointOptions Base() => new() { ApiKey = "sk-or-OPENROUTER", CacheDirectory = _cache };

    [Theory]
    [InlineData("ollama:llama3.2", "ollama", "llama3.2")]
    [InlineData("openrouter:vendor/alpha", "openrouter", "vendor/alpha")]
    [InlineData("lmstudio:qwen3:batch", "lmstudio", "qwen3:batch")]
    [InlineData("vendor/alpha", null, "vendor/alpha")]
    [InlineData("deepseek/deepseek-r1:free", null, "deepseek/deepseek-r1:free")]
    public void Splits_a_specification_into_endpoint_and_model(string spec, string? endpoint, string model) =>
        Assert.Equal((endpoint, model), EndpointRegistry.Split(spec));

    [Fact]
    public void Resolves_address_dialect_flat_rate_and_key_of_an_endpoint()
    {
        var options = Registry(new() { ["SERVER_KEY"] = "secret-server" }).Resolve("Ollama", Base())!;

        Assert.Equal(("ollama", EndpointKind.OpenAiCompatible, true), (options.EndpointName, options.Kind, options.FlatRate));
        Assert.Equal("http://localhost:11434/v1/", options.BaseAddress.AbsoluteUri);
        Assert.Equal("secret-server", options.ApiKey);
        Assert.DoesNotContain("secret-server", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_key_from_a_variable_named_after_the_endpoint_when_none_is_configured()
    {
        var options = Registry(new() { ["LLMSHOOTOUT_LMSTUDIO_API_KEY"] = "k2" }).Resolve("lmstudio", Base())!;

        Assert.Equal("k2", options.ApiKey);
        Assert.False(options.FlatRate);
    }

    [Fact]
    public void An_endpoint_without_a_key_is_allowed_and_openrouter_keeps_the_base_options()
    {
        var registry = Registry();

        Assert.Null(registry.Resolve("lmstudio", Base())!.ApiKey);
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
    [InlineData("random")]
    [InlineData("Scripted")]
    [InlineData("openrouter")]
    public void A_reserved_name_cannot_be_an_endpoint(string name)
    {
        var definitions = new Dictionary<string, EndpointDefinition> { [name] = new() { BaseAddress = new Uri("http://localhost/v1") } };

        var ex = Assert.Throws<ArgumentException>(() => new EndpointRegistry(definitions));

        Assert.Contains("reserved", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ollama", "LLMSHOOTOUT_OLLAMA_API_KEY")]
    [InlineData("my-server.1", "LLMSHOOTOUT_MY_SERVER_1_API_KEY")]
    public void The_default_key_variable_is_derived_from_the_name(string name, string variable) =>
        Assert.Equal(variable, EndpointRegistry.DefaultKeyVariable(name));

    [Fact]
    public async Task Other_endpoints_get_no_openrouter_fields_and_a_top_level_reasoning_effort()
    {
        var options = Registry(new() { ["SERVER_KEY"] = "pk" }).Resolve("ollama", Base())! with { ReasoningEffort = "low", RetryDelay = TimeSpan.FromMilliseconds(1) };
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}", cost: null));
        var player = CreatePlayer(handler, options, schema: true);

        await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], JsonNode.Parse("""{"type":"object"}"""), 1, _ => "{}"), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Null(body["provider"]);
        Assert.Null(body["reasoning"]);
        Assert.Equal("low", body["reasoning_effort"]!.ToString());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.False(handler.Messages[0].Headers.Contains("X-Title"));
        Assert.Equal("Bearer pk", handler.Messages[0].Headers.GetValues("Authorization").Single());
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Messages[0].RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, "0")]
    [InlineData(false, null)]
    public async Task Without_a_reported_cost_a_flat_rate_call_counts_as_zero_and_any_other_stays_unknown(bool flatRate, string? expected)
    {
        var options = Registry(flatRate: flatRate).Resolve("ollama", Base())!;
        var player = CreatePlayer(new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}", cost: null)), options);

        var reply = await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken);

        Assert.Null(reply.Call!.CostUsd);
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), reply.Call.EstimatedCostUsd);
        Assert.Equal("ollama:vendor/model", player.Name);
    }

    [Fact]
    public async Task An_endpoint_message_about_missing_structured_output_is_kept_as_a_notice()
    {
        const string Body = """{"id":"x","object":"chat.completion","model":"llama3.2","choices":[{"index":0,"message":{"role":"assistant","content":null,"refusal":"Could not produce valid structured output: no JSON object found in the response"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var options = Registry().Resolve("ollama", Base())!;
        var player = CreatePlayer(new FakeEndpointHandler().Ok(Body), options);

        var reply = await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken);

        Assert.Null(reply.Content);
        Assert.Contains("no JSON object found", reply.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_without_a_key_is_called_without_an_authorization_header()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}", cost: null));
        var player = CreatePlayer(handler, Registry().Resolve("lmstudio", Base())!);

        await player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken);

        Assert.Null(handler.Messages[0].Headers.Authorization);
    }

    [Fact]
    public async Task A_failure_names_the_endpoint_not_openrouter()
    {
        var options = Registry().Resolve("ollama", Base())! with { MaxHttpRetries = 0 };
        var player = CreatePlayer(new FakeEndpointHandler().Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}"""), options);

        var ex = await Assert.ThrowsAsync<PlayerFailureException>(() => player.RespondAsync(new PlayerCall(CallPurpose.Turn, [new PromptMessage(PromptRole.User, "go")], null, 1, _ => "{}"), TestContext.Current.CancellationToken));

        Assert.Contains("endpoint 'ollama' returned HTTP 401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_list_of_another_endpoint_has_only_ids_and_counts_as_schema_capable()
    {
        var models = ModelCatalog.Parse(ServerModels, EndpointKind.OpenAiCompatible);

        Assert.Equal(["llama3.2", "llama3.3"], models.Select(m => m.Id));
        Assert.All(models, m => Assert.True(m.SupportsJsonSchema));
        Assert.All(models, m => Assert.Equal(0m, m.PromptUsdPerMillion));
    }

    [Fact]
    public async Task Every_endpoint_has_its_own_model_list_cache()
    {
        var ollama = Registry().Resolve("ollama", Base())!;
        var lmstudio = Registry().Resolve("lmstudio", Base())!;
        var token = TestContext.Current.CancellationToken;

        await Catalog(new FakeEndpointHandler().Ok(ServerModels), ollama).GetModelsAsync(token);
        await Catalog(new FakeEndpointHandler().Ok("""{"data":[{"id":"qwen3"}]}"""), lmstudio).GetModelsAsync(token);

        Assert.Equal(["llama3.2", "llama3.3"], (await Catalog(new FakeEndpointHandler(), ollama).GetModelsAsync(token)).Select(m => m.Id));
        Assert.Equal(["qwen3"], (await Catalog(new FakeEndpointHandler(), lmstudio).GetModelsAsync(token)).Select(m => m.Id));
        Assert.True(File.Exists(Path.Combine(_cache, "models.ollama.json")));
    }

    [Fact]
    public async Task The_factory_creates_a_player_for_an_endpoint_without_an_openrouter_key()
    {
        var factory = Factory(Base() with { ApiKey = null }, new FakeEndpointHandler().Ok(ServerModels));

        var player = await factory.CreateAsync("ollama:llama3.2", 1, TestContext.Current.CancellationToken);

        Assert.Equal("ollama:llama3.2", player.Name);
        Assert.Equal("llama3.2", player.Model!.Slug);
    }

    [Fact]
    public async Task Settings_that_openrouter_would_check_are_not_checked_against_a_list_without_parameters()
    {
        var factory = Factory(Base() with { Temperature = 0.4f }, new FakeEndpointHandler().Ok(ServerModels));

        var player = await factory.CreateAsync("ollama:llama3.2", 1, TestContext.Current.CancellationToken);

        Assert.Equal("0.4", player.Settings["temperature"]);
    }

    [Fact]
    public async Task An_unknown_endpoint_lists_the_configured_ones()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Base(), new FakeEndpointHandler()).CreateAsync("gemini:x", 1, TestContext.Current.CancellationToken));

        Assert.Contains("Unknown endpoint 'gemini'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("openrouter, ollama, lmstudio, broken", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_model_points_to_the_model_list_of_the_endpoint()
    {
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => Factory(Base(), new FakeEndpointHandler().Ok(ServerModels)).CreateAsync("ollama:gpt-9", 1, TestContext.Current.CancellationToken));

        Assert.Contains("shootout models --endpoint ollama", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_whose_list_is_unreachable_is_trusted()
    {
        var factory = Factory(Base(), new FakeEndpointHandler().Respond(HttpStatusCode.ServiceUnavailable, "{}"));

        var player = await factory.CreateAsync("ollama:anything", 1, TestContext.Current.CancellationToken);

        Assert.Equal("ollama:anything", player.Name);
    }

    [Fact]
    public async Task An_openrouter_model_above_the_price_ceiling_is_refused()
    {
        const string Models = """{"data":[{"id":"vendor/cheap","pricing":{"prompt":"0.000001","completion":"0.000002"},"supported_parameters":["structured_outputs"]},{"id":"vendor/dear","pricing":{"prompt":"0.00001","completion":"0.00008"},"supported_parameters":["structured_outputs"]}]}""";
        var options = Base();
        var factory = new PlayerFactory(options, Catalog(new FakeEndpointHandler().Ok(Models).Ok(Models), options), TimeProvider.System, null, Registry(), null, 40m);

        var cheap = await factory.CreateAsync("vendor/cheap", 1, TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<PlayerSpecException>(() => factory.CreateAsync("vendor/dear", 1, TestContext.Current.CancellationToken));

        Assert.Equal("openrouter:vendor/cheap", cheap.Name);
        Assert.Contains("above the limit of 40", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ollama:llama3.2", "llama3.2")]
    [InlineData("openrouter:openai/gpt-4o", "gpt-4o")]
    [InlineData("random", "random")]
    [InlineData("scripted:/tmp/a.json", "a.json")]
    public void File_names_do_not_contain_the_endpoint(string player, string slug) =>
        Assert.Equal(slug, MatchPaths.Slug(player));

    private static ModelCatalog Catalog(FakeEndpointHandler handler, EndpointOptions options) =>
        new(new HttpClient(handler) { BaseAddress = options.BaseAddress }, options, TimeProvider.System);

    private static PlayerFactory Factory(EndpointOptions options, FakeEndpointHandler listHandler) =>
        new(options, Catalog(new FakeEndpointHandler(), options), TimeProvider.System, null, Registry(), endpoint => Catalog(listHandler, endpoint));

    private static ChatPlayer CreatePlayer(FakeEndpointHandler handler, EndpointOptions options, bool schema = false)
    {
        var http = ChatClientFactory.CreateHttpClient(options, handler);
        return new ChatPlayer(ChatClientFactory.CreateChatClient(options, "vendor/model", http), "vendor/model", options, schema, TimeProvider.System);
    }
}
