using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>
/// Adds the endpoint specific fields that <c>ChatOptions</c> cannot carry. When a JSON schema is requested it
/// makes the schema strict; for OpenRouter it also sets <c>provider.require_parameters</c>, so OpenRouter only routes
/// to providers that honor the schema. The reasoning effort is added only when configured: as <c>reasoning.effort</c>
/// for OpenRouter and as <c>reasoning_effort</c> for other OpenAI-compatible endpoints.
/// </summary>
public sealed class OpenRouterRequestHandler(OpenRouterOptions options) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Content is not null && request.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (JsonNode.Parse(body) is JsonObject json)
            {
                Rewrite(json);
                request.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            }
        }

        if (options.Kind == EndpointKind.OpenRouter)
        {
            request.Headers.Remove("X-Title");
            request.Headers.Remove("HTTP-Referer");
            request.Headers.TryAddWithoutValidation("X-Title", "LLM Shootout");
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/chr0mcom/LLM-Shootout");
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private void Rewrite(JsonObject json)
    {
        var openRouter = options.Kind == EndpointKind.OpenRouter;
        if (json["response_format"]?["json_schema"] is JsonObject schema)
        {
            schema["strict"] = true;
            if (openRouter)
            {
                json["provider"] = new JsonObject { ["require_parameters"] = true };
            }
        }

        if (options.ReasoningEffort is { } effort)
        {
            if (openRouter)
            {
                json["reasoning"] = new JsonObject { ["effort"] = effort };
            }
            else
            {
                json["reasoning_effort"] = effort;
            }
        }
    }
}
