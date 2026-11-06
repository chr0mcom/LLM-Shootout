using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>
/// Adds the OpenRouter specific fields that <c>ChatOptions</c> cannot carry. When a JSON schema is requested it
/// makes the schema strict and sets <c>provider.require_parameters</c>, so OpenRouter only routes to providers that
/// honor the schema. Sampling and reasoning settings are added only when configured.
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

        request.Headers.Remove("X-Title");
        request.Headers.Remove("HTTP-Referer");
        request.Headers.TryAddWithoutValidation("X-Title", "LLM Shootout");
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/chr0mcom/LLM-Shootout");
        return await base.SendAsync(request, cancellationToken);
    }

    private void Rewrite(JsonObject json)
    {
        if (json["response_format"]?["json_schema"] is JsonObject schema)
        {
            schema["strict"] = true;
            json["provider"] = new JsonObject { ["require_parameters"] = true };
        }

        if (options.ReasoningEffort is { } effort)
        {
            json["reasoning"] = new JsonObject { ["effort"] = effort };
        }
    }
}
