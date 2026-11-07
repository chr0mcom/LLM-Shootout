using System.Globalization;
using System.Text;

namespace LlmShootout.Arena.ModelAccess;

/// <summary>The dialect an endpoint speaks.</summary>
public enum EndpointKind
{
    /// <summary>OpenRouter: extra request fields (<c>provider</c>, <c>reasoning</c>), cost and provider in the usage block, model list with prices.</summary>
    OpenRouter,

    /// <summary>Any other OpenAI-compatible <c>chat/completions</c> API, for example the CLI proxy of this server.</summary>
    OpenAiCompatible,
}

/// <summary>
/// An additional endpoint from the configuration, addressed in a player specification as <c>&lt;name&gt;:&lt;model&gt;</c>.
/// The API key is never part of the definition: it is read from the environment variable named here.
/// </summary>
public sealed record EndpointDefinition
{
    /// <summary>Base address of the API, including the version segment, for example <c>http://localhost:11434/v1</c>.</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>The dialect. Anything that is not OpenRouter is <see cref="EndpointKind.OpenAiCompatible"/>.</summary>
    public EndpointKind Kind { get; init; } = EndpointKind.OpenAiCompatible;

    /// <summary>
    /// Name of the environment variable that holds the key. Without it, <c>LLMSHOOTOUT_&lt;NAME&gt;_API_KEY</c> is read.
    /// An endpoint without any key is called without credentials.
    /// </summary>
    public string? ApiKeyVariable { get; init; }

    /// <summary>
    /// True if calls are covered by a subscription and cost nothing per call. Then a call without a reported cost
    /// counts as 0 USD instead of "unknown", and the spending limit does not reserve money for it.
    /// </summary>
    public bool FlatRate { get; init; }
}

/// <summary>The endpoints from the configuration. <c>openrouter</c> always exists and uses the base options.</summary>
public sealed class EndpointRegistry
{
    private readonly Dictionary<string, EndpointDefinition> _definitions;
    private readonly Func<string, string?> _environment;

    /// <summary>Creates the registry. <paramref name="environment"/> reads environment variables (replaced in tests).</summary>
    public EndpointRegistry(IEnumerable<KeyValuePair<string, EndpointDefinition>>? definitions = null, Func<string, string?>? environment = null)
    {
        _definitions = new Dictionary<string, EndpointDefinition>(definitions ?? [], StringComparer.OrdinalIgnoreCase);
        if (_definitions.Keys.FirstOrDefault(name => ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) is { } reserved)
        {
            throw new ArgumentException($"The endpoint name '{reserved}' is reserved: it already means something in a player specification. Choose another name.", nameof(definitions));
        }

        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    private static readonly string[] ReservedNames = ["openrouter", "random", "optimal", "scripted"];

    /// <summary>The name of the built-in OpenRouter endpoint.</summary>
    public const string OpenRouterName = "openrouter";

    /// <summary>The names of the configured endpoints, without OpenRouter.</summary>
    public IReadOnlyCollection<string> Names => _definitions.Keys;

    /// <summary>
    /// The options for calls to <paramref name="name"/>: the base options with address, dialect, key and flat-rate flag of
    /// the endpoint. Null if there is no such endpoint.
    /// </summary>
    public EndpointOptions? Resolve(string name, EndpointOptions baseOptions)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);
        if (string.Equals(name, OpenRouterName, StringComparison.OrdinalIgnoreCase))
        {
            return baseOptions;
        }

        if (!_definitions.TryGetValue(name, out var definition))
        {
            return null;
        }

        if (definition.BaseAddress is not { IsAbsoluteUri: true, Scheme: "http" or "https" } address)
        {
            throw new PlayerSpecException($"Endpoint '{name}' needs an absolute http(s) BaseAddress, for example http://localhost:11434/v1.");
        }

        var variable = definition.ApiKeyVariable ?? DefaultKeyVariable(name);
        return baseOptions with
        {
            EndpointName = name.ToLowerInvariant(),
            BaseAddress = address,
            Kind = definition.Kind,
            FlatRate = definition.FlatRate,
            ApiKey = _environment(variable),
        };
    }

    /// <summary>The variable that holds the key of an endpoint when the definition names none: <c>LLMSHOOTOUT_OLLAMA_API_KEY</c> for <c>ollama</c>.</summary>
    public static string DefaultKeyVariable(string name)
    {
        var builder = new StringBuilder("LLMSHOOTOUT_");
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpper(c, CultureInfo.InvariantCulture) : '_');
        }

        return builder.Append("_API_KEY").ToString();
    }

    /// <summary>
    /// Splits <c>ollama:llama3.2</c> into endpoint and model. A model slug of OpenRouter has a slash
    /// before any colon (<c>deepseek/deepseek-r1:free</c>), so such a text is a model without endpoint.
    /// </summary>
    public static (string? Endpoint, string Model) Split(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var colon = spec.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && !spec[..colon].Contains('/', StringComparison.Ordinal) && !spec[..colon].Contains(' ', StringComparison.Ordinal)
            ? (spec[..colon], spec[(colon + 1)..])
            : (null, spec);
    }
}
