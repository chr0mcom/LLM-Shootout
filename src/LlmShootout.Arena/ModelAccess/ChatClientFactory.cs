using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using Polly;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>
/// Builds the HTTP and chat pipeline for one endpoint (OpenRouter or any OpenAI-compatible API). From the outside in:
/// <c>ChatClientBuilder</c> (logging) around the OpenAI chat client, whose transport is an
/// <see cref="HttpClient"/> with these handlers: resilience (one timeout, one retry layer), request rewriting
/// (endpoint specific fields), raw capture (exchange without credentials), and the socket handler.
/// </summary>
public static class ChatClientFactory
{
    /// <summary>Creates the <see cref="HttpClient"/>. <paramref name="inner"/> replaces the network in tests.</summary>
    public static HttpClient CreateHttpClient(EndpointOptions options, HttpMessageHandler? inner = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capture = new RawCaptureHandler { InnerHandler = inner ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) } };
        var rewrite = new EndpointRequestHandler(options) { InnerHandler = capture };
        var resilience = new ResilienceHandler(BuildPipeline(options)) { InnerHandler = rewrite };
        return new HttpClient(resilience) { BaseAddress = options.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Creates the chat client for one model.</summary>
    public static IChatClient CreateChatClient(EndpointOptions options, string modelSlug, HttpClient httpClient, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSlug);
        ArgumentNullException.ThrowIfNull(httpClient);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = options.BaseAddress,
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = WholeCallBudget(options),
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
    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(EndpointOptions options)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        if (options.MaxHttpRetries > 0)
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxHttpRetries,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = false,
                ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            });
        }

        return builder.AddTimeout(options.CallTimeout).Build();
    }

    /// <summary>
    /// The client-level timeout spans all HTTP attempts of a call, so it must be larger than every attempt plus the backoff
    /// between them; the timeout of one attempt is the job of the resilience pipeline.
    /// </summary>
    internal static TimeSpan WholeCallBudget(EndpointOptions options) =>
        (options.CallTimeout * (options.MaxHttpRetries + 1)) + (options.RetryDelay * Math.Pow(2, options.MaxHttpRetries + 1)) + TimeSpan.FromSeconds(30);

    private static bool IsRetryable(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError } } => true,
        { Result: { } response } => response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500,
        _ => false,
    };
}
