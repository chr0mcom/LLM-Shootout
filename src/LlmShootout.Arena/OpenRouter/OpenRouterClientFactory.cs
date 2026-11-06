using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using Polly;
using Polly.Retry;

namespace LlmShootout.Arena.OpenRouter;

/// <summary>
/// Builds the HTTP and chat pipeline for OpenRouter. From the outside in:
/// <c>ChatClientBuilder</c> (logging) around the OpenAI chat client, whose transport is an
/// <see cref="HttpClient"/> with these handlers: resilience (one timeout, one retry layer), request rewriting
/// (OpenRouter fields), raw capture (exchange without credentials), and the socket handler.
/// </summary>
public static class OpenRouterClientFactory
{
    /// <summary>Creates the <see cref="HttpClient"/>. <paramref name="inner"/> replaces the network in tests.</summary>
    public static HttpClient CreateHttpClient(OpenRouterOptions options, HttpMessageHandler? inner = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capture = new RawCaptureHandler { InnerHandler = inner ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) } };
        var rewrite = new OpenRouterRequestHandler(options) { InnerHandler = capture };
        var resilience = new ResilienceHandler(BuildPipeline(options)) { InnerHandler = rewrite };
        return new HttpClient(resilience) { BaseAddress = options.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Creates the chat client for one model.</summary>
    public static IChatClient CreateChatClient(OpenRouterOptions options, string modelSlug, HttpClient httpClient, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);
        ArgumentNullException.ThrowIfNull(httpClient);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = options.BaseAddress,
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = options.CallTimeout + TimeSpan.FromSeconds(30),
        };
        var openAi = new OpenAIClient(new ApiKeyCredential(options.ApiKey ?? "missing"), clientOptions);
        var builder = new ChatClientBuilder(openAi.GetChatClient(modelSlug).AsIChatClient());
        return builder.UseLogging(loggerFactory ?? NullLoggerFactory.Instance).Build();
    }

    /// <summary>
    /// The only retry layer of the application: HTTP 429, 5xx and failures to connect, with exponential backoff.
    /// Inside the retry sits the timeout of one attempt. A timeout is not retried, because the cancelled call may
    /// already be paid for; it ends the call. A connection that breaks while the answer is being read is not
    /// retried either, for the same reason.
    /// </summary>
    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(OpenRouterOptions options) =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxHttpRetries,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = false,
                ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            })
            .AddTimeout(options.CallTimeout)
            .Build();

    private static bool IsRetryable(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError } } => true,
        { Result: { } response } => response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500,
        _ => false,
    };
}
