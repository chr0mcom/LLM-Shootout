using System.Net;
using System.Text;

namespace LlmShootout.Tests.OpenRouter;

public sealed class FakeOpenRouterHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public List<string> Requests { get; } = [];

    public List<HttpRequestMessage> Messages { get; } = [];

    public FakeOpenRouterHandler Respond(HttpStatusCode status, string body)
    {
        _script.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        return this;
    }

    public FakeOpenRouterHandler Ok(string body) => Respond(HttpStatusCode.OK, body);

    public FakeOpenRouterHandler Hang()
    {
        _script.Enqueue(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        return this;

    }

    public FakeOpenRouterHandler Throw(Exception exception)
    {
        _script.Enqueue((_, _) => throw exception);
        return this;
    }

    public static string Completion(
        string? content,
        string? reasoning = null,
        decimal cost = 0.00123m,
        string model = "vendor/model-2026-10-01",
        string provider = "TestProvider",
        string finish = "stop")
    {
        var message = new System.Text.Json.Nodes.JsonObject { ["role"] = "assistant", ["content"] = content, ["reasoning"] = reasoning };
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "gen-abc123",
            ["object"] = "chat.completion",
            ["created"] = 1_700_000_000,
            ["model"] = model,
            ["provider"] = provider,
            ["choices"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["index"] = 0, ["finish_reason"] = finish, ["message"] = message }),
            ["usage"] = new System.Text.Json.Nodes.JsonObject
            {
                ["prompt_tokens"] = 120,
                ["completion_tokens"] = 80,
                ["total_tokens"] = 200,
                ["cost"] = cost,
                ["completion_tokens_details"] = new System.Text.Json.Nodes.JsonObject { ["reasoning_tokens"] = 50 },
            },
        };
        return body.ToJsonString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        Messages.Add(request);
        if (!_script.TryDequeue(out var next))
        {
            throw new InvalidOperationException("No response scripted.");
        }

        return await next(request, cancellationToken);
    }
}
