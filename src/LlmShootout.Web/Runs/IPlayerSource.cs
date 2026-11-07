using LlmShootout.Arena.Players;

namespace LlmShootout.Web.Runs;

/// <summary>Where the web interface gets its players and the model names it suggests. Tests replace it.</summary>
internal interface IPlayerSource
{
    /// <summary>Creates the player named by a specification such as <c>random</c>, <c>ollama:llama3.2</c> or <c>openai/gpt-4o</c>.</summary>
    Task<IPlayer> CreateAsync(string spec, long seed, CancellationToken cancellationToken);

    /// <summary>Player specifications for the form: <c>random</c>, every model of every configured endpoint, and OpenRouter slugs if a key is set.</summary>
    Task<IReadOnlyList<string>> SuggestionsAsync(CancellationToken cancellationToken);
}
