using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Matches;

/// <summary>The state of one running match, shared by the runner and the turn executor.</summary>
public sealed class MatchContext(
    MatchPlan plan,
    IGame game,
    PromptBuilder prompts,
    AnswerEvaluator evaluator,
    ProtocolWriter writer,
    PlayerGateway gateway,
    Identity.IdentityGuard identityGuard,
    TimeProvider time)
{
    /// <summary>The plan.</summary>
    public MatchPlan Plan { get; } = plan;

    /// <summary>The running game.</summary>
    public IGame Game { get; } = game;

    /// <summary>Builds prompts and renders wording.</summary>
    public PromptBuilder Prompts { get; } = prompts;

    /// <summary>Checks answers.</summary>
    public AnswerEvaluator Evaluator { get; } = evaluator;

    /// <summary>The protocol.</summary>
    public ProtocolWriter Writer { get; } = writer;

    /// <summary>The door to all players.</summary>
    public PlayerGateway Gateway { get; } = gateway;

    /// <summary>Marks identity mentions in public messages.</summary>
    public Identity.IdentityGuard IdentityGuard { get; } = identityGuard;

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; } = time;

    /// <summary>The shared history.</summary>
    public Timeline Timeline { get; } = new();

    /// <summary>The private thoughts.</summary>
    public ThoughtLog Thoughts { get; } = new();

    /// <summary>Language of the prompts.</summary>
    public string Language => Plan.Setup.Language;
}
