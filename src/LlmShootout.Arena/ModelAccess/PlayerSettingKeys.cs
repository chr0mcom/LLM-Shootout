namespace LlmShootout.Arena.ModelAccess;

/// <summary>The keys under which the settings of a model player are recorded in the protocol (<c>match_started.players.*.settings</c>).</summary>
public static class PlayerSettingKeys
{
    /// <summary>Seconds allowed for one call.</summary>
    public const string CallTimeoutSeconds = "callTimeoutSeconds";

    /// <summary>Sampling temperature; only present if it was sent.</summary>
    public const string Temperature = "temperature";

    /// <summary>Reasoning effort; only present if it was sent.</summary>
    public const string ReasoningEffort = "reasoningEffort";

    /// <summary>Output token limit; only present if it was sent.</summary>
    public const string MaxOutputTokens = "maxOutputTokens";
}
