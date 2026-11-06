namespace LlmShootout.Arena;

/// <summary>Settings of one match that are independent of the game.</summary>
public sealed record MatchOptions
{
    /// <summary>Model calls per turn: first try plus retries, shared by invalid answers and refusals.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Longest private thought. A longer one is shortened and marked, never rejected.</summary>
    public int MaxThoughtWords { get; init; } = 40;

    /// <summary>Longest public message. A longer one is invalid, except in the last attempt, where it is shortened.</summary>
    public int MaxMessageWords { get; init; } = 20;

    /// <summary>How many of its own earlier thoughts a seat sees again. 0 shows none.</summary>
    public int ThoughtMemory { get; init; } = 10;

    /// <summary>True to name the opponent model in the prompt. Default: hidden.</summary>
    public bool RevealIdentity { get; init; }

    /// <summary>True to replace identity hits in public messages by a placeholder. Default: only mark them.</summary>
    public bool RedactIdentity { get; init; }

    /// <summary>True to run the debriefing after the game.</summary>
    public bool Debrief { get; init; } = true;

    /// <summary>Spending limit for one run of the application, in USD.</summary>
    public decimal MaxCostUsd { get; init; } = 1m;

    /// <summary>Keywords (lower case) that mark an unusable answer as a refusal.</summary>
    public IReadOnlyList<string> RefusalKeywords { get; init; } = Answers.RefusalDetector.DefaultKeywords;
}
