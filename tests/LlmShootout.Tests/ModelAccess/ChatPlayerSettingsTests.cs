using LlmShootout.Arena.ModelAccess;

namespace LlmShootout.Tests.ModelAccess;

public sealed class ChatPlayerSettingsTests
{
    [Fact]
    public void A_chat_player_describes_its_configured_settings()
    {
        var options = new EndpointOptions { ApiKey = "k", Temperature = 0.2f, ReasoningEffort = "low", MaxOutputTokens = 500, CallTimeout = TimeSpan.FromSeconds(90) };
        var http = ChatClientFactory.CreateHttpClient(options, new Tests.ModelAccess.FakeEndpointHandler());
        var player = new ChatPlayer(ChatClientFactory.CreateChatClient(options, "x/y", http), "x/y", options, false, TimeProvider.System);

        Assert.Equal(("0.2", "low", "500", "90"), (player.Settings["temperature"], player.Settings["reasoningEffort"], player.Settings["maxOutputTokens"], player.Settings["callTimeoutSeconds"]));
    }

    [Fact]
    public void The_client_level_timeout_spans_every_http_attempt_plus_backoff()
    {
        var options = new EndpointOptions { CallTimeout = TimeSpan.FromSeconds(180), MaxHttpRetries = 3, RetryDelay = TimeSpan.FromSeconds(2) };

        var budget = ChatClientFactory.WholeCallBudget(options);

        Assert.True(budget >= TimeSpan.FromSeconds(4 * 180));
        Assert.True(budget > options.CallTimeout + TimeSpan.FromSeconds(30));
    }
}
