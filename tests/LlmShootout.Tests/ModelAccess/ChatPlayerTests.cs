using System.Net;
using System.Text.Json.Nodes;
using LlmShootout.Arena.ModelAccess;
using LlmShootout.Arena.Players;

namespace LlmShootout.Tests.ModelAccess;

public class ChatPlayerTests
{
    private const string Key = "sk-or-v1-SUPERSECRETKEY";

    private static EndpointOptions Options(Action<EndpointOptions>? _ = null, TimeSpan? timeout = null, float? temperature = null, string? effort = null) =>
        new()
        {
            ApiKey = Key,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            CallTimeout = timeout ?? TimeSpan.FromSeconds(20),
            Temperature = temperature,
            ReasoningEffort = effort,
        };

    private static ChatPlayer Create(FakeEndpointHandler handler, EndpointOptions options, bool schema = false, string slug = "vendor/model")
    {
        var http = ChatClientFactory.CreateHttpClient(options, handler);
        return new ChatPlayer(ChatClientFactory.CreateChatClient(options, slug, http), slug, options, schema, TimeProvider.System);
    }

    private static PlayerCall Call(JsonNode? schema = null) =>
        new(CallPurpose.Turn, [new PromptMessage(PromptRole.System, "rules"), new PromptMessage(PromptRole.User, "your move")], schema, 1, _ => "{}");

    [Fact]
    public async Task Maps_content_cost_provider_generation_id_tokens_and_delivered_model()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{\"move\":\"split\"}", reasoning: "hmm"));
        var player = Create(handler, Options());

        var reply = await player.RespondAsync(Call(), TestContext.Current.CancellationToken);

        Assert.Equal("{\"move\":\"split\"}", reply.Content);
        Assert.Equal("hmm", reply.Reasoning);
        var info = reply.Call!;
        Assert.Equal(0.00123m, info.CostUsd);
        Assert.Equal("TestProvider", info.Provider);
        Assert.Equal("gen-abc123", info.GenerationId);
        Assert.Equal("vendor/model", info.RequestedModel);
        Assert.Equal("vendor/model-2026-10-01", info.DeliveredModel);
        Assert.Equal((120, 80, 50), (info.PromptTokens, info.CompletionTokens, info.ReasoningTokens));
        Assert.Equal("stop", info.FinishReason);
        Assert.Equal(1, info.HttpAttempts);
        Assert.Equal("vendor/model-2026-10-01", player.Model!.Version);
    }

    [Fact]
    public async Task Sends_model_messages_and_openrouter_headers_but_no_optional_parameters_by_default()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}"));
        await Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Equal("vendor/model", body["model"]!.ToString());
        Assert.Equal(2, body["messages"]!.AsArray().Count);
        Assert.Null(body["temperature"]);
        Assert.Null(body["provider"]);
        Assert.Null(body["reasoning"]);
        Assert.Null(body["response_format"]);
        Assert.Contains("LLM Shootout", handler.Messages[0].Headers.GetValues("X-Title"));
    }

    [Fact]
    public async Task Sends_sampling_and_reasoning_only_when_configured()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}"));
        await Create(handler, Options(temperature: 0.2f, effort: "low")).RespondAsync(Call(), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Equal(0.2, body["temperature"]!.GetValue<double>(), 3);
        Assert.Equal("low", body["reasoning"]!["effort"]!.ToString());
    }

    [Fact]
    public async Task Json_schema_makes_the_request_strict_and_requires_matching_providers()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}"));
        var schema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"move\":{\"type\":\"string\"}}}");
        await Create(handler, Options(), schema: true).RespondAsync(Call(schema), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Equal("json_schema", body["response_format"]!["type"]!.ToString());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.True(body["provider"]!["require_parameters"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Without_schema_support_the_format_stays_in_the_prompt_only()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}"));
        var schema = JsonNode.Parse("{\"type\":\"object\"}");
        await Create(handler, Options(), schema: false).RespondAsync(Call(schema), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0])!.AsObject();
        Assert.Null(body["response_format"]);
        Assert.Null(body["provider"]);
    }

    [Fact]
    public async Task The_raw_exchange_contains_the_bodies_but_never_the_api_key()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion("{}"));
        var reply = await Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken);

        Assert.Contains("your move", reply.RawExchange, StringComparison.Ordinal);
        Assert.Contains("gen-abc123", reply.RawExchange, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, reply.RawExchange, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", reply.RawExchange, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"Bearer {Key}", handler.Messages[0].Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task Empty_content_keeps_the_reasoning_for_recovery()
    {
        var handler = new FakeEndpointHandler().Ok(FakeEndpointHandler.Completion(null, reasoning: "so... {\"move\":\"steal\"}"));
        var reply = await Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken);

        Assert.Null(reply.Content);
        Assert.Contains("steal", reply.Reasoning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_429_and_5xx_are_retried_by_the_resilience_layer_and_counted()
    {
        var handler = new FakeEndpointHandler()
            .Respond(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"slow down\"}}")
            .Respond(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"boom\"}}")
            .Ok(FakeEndpointHandler.Completion("{}"));

        var reply = await Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken);

        Assert.Equal(3, reply.Call!.HttpAttempts);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_5xx_becomes_a_technical_failure_after_the_configured_retries()
    {
        var handler = new FakeEndpointHandler();
        for (var i = 0; i < 4; i++)
        {
            handler.Respond(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"boom\"}}");
        }

        var ex = await Assert.ThrowsAsync<PlayerFailureException>(() => Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken));

        Assert.False(ex.IsTimeout);
        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var handler = new FakeEndpointHandler().Respond(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"no key\"}}");

        var ex = await Assert.ThrowsAsync<PlayerFailureException>(() => Create(handler, Options()).RespondAsync(Call(), TestContext.Current.CancellationToken));

        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_slow_call_times_out_and_is_not_retried()
    {
        var handler = new FakeEndpointHandler().Hang();

        var ex = await Assert.ThrowsAsync<PlayerFailureException>(
            () => Create(handler, Options(timeout: TimeSpan.FromMilliseconds(300))).RespondAsync(Call(), TestContext.Current.CancellationToken));

        Assert.True(ex.IsTimeout);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        var handler = new FakeEndpointHandler().Hang();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler, Options()).RespondAsync(Call(), cts.Token));
    }
}
