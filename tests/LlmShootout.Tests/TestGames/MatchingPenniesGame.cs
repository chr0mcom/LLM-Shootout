using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>The minimal game of docs/adding-a-game.md: both seats pick heads or tails at the same time; A wins on a match, B on a mismatch.</summary>
public sealed class MatchingPenniesDefinition : IGameDefinition
{
    public string Id => "matching-pennies";

    public string DisplayName => "Matching Pennies";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            Both players secretly choose "heads" or "tails". Seat A wins if the choices match, seat B wins if they differ.
            ## view
            One round is played.
            ## instruction
            Choose now.
            ## format
            Exactly one of "heads" or "tails".
            ## error.move
            "{move}" is neither heads nor tails
            ## fact.choice
            Choice ({who}): {choice}
            """,
    });

    public IGame Create(GameSetup setup) => new MatchingPenniesGame();

    public IReadOnlyList<Arena.Debriefing.DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];
}

public sealed class MatchingPenniesGame : IGame
{
    private static readonly string[] Moves = ["heads", "tails"];

    public GameResult? Result { get; private set; }

    public IReadOnlyList<TurnRequest> PendingTurns => Result is not null
        ? []
        : [.. Seat.All.Select(seat => new TurnRequest(
            $"choose-{seat}",
            "choose",
            seat,
            FieldRule.Forbidden,
            FieldRule.Required,
            TemplateRef.Of("instruction"),
            new MoveFormat(TemplateRef.Of("format"), Moves)))];

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules");

    public TemplateRef ViewFor(Seat seat) => TemplateRef.Of("view");

    public MoveCheck Check(TurnRequest turn, string move)
    {
        var normalized = move.Trim().ToLowerInvariant();
        return Moves.Contains(normalized)
            ? MoveCheck.Ok(normalized)
            : MoveCheck.Reject("not-allowed-value", TemplateRef.Of("error.move", ("move", move)), "\"heads\", \"tails\"");
    }

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        var choices = submissions.ToDictionary(s => s.Seat, s => s.Move);
        var (winner, outcome) = Decide(choices[Seat.A], choices[Seat.B]);
        Result = new GameResult(
            outcome,
            Seat.All.ToDictionary(seat => seat, seat => new SeatResult(seat == winner ? 1 : 0, seat == winner ? "win" : "loss")));
        return new TurnResolution([.. submissions.Select(s => new GameFact(
            "choice",
            FactAudience.Everyone,
            s.Seat,
            TemplateRef.Of("fact.choice", ("choice", s.Move ?? "-")),
            new Dictionary<string, string> { ["choice"] = s.Move ?? "-" }))]);
    }

    /// <summary>A seat that gave no valid choice loses; if both failed, nobody wins.</summary>
    private static (Seat? Winner, string Outcome) Decide(string? a, string? b) => (a, b) switch
    {
        (null, null) => (null, "no_decision"),
        (null, _) => (Seat.B, "no_decision_a"),
        (_, null) => (Seat.A, "no_decision_b"),
        _ => a == b ? (Seat.A, "match") : (Seat.B, "mismatch"),
    };

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => Moves[rng.NextInt(Moves.Length)];
}
