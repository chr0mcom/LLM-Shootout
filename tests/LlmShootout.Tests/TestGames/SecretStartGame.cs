using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>Test game with a hidden starting state: each seat draws a secret card at the start; then both name a card at once.</summary>
public sealed class SecretStartDefinition : IGameDefinition
{
    public string Id => "secret-start";

    public string DisplayName => "Secret start";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            Each player holds a secret card. Then both name a number at once.
            ## view
            Name a number.
            ## instruction
            Name a number from 1 to 9.
            ## format
            An integer.
            ## error.move
            "{move}" is not a number from 1 to 9
            ## fact.card
            Your secret card is {card}.
            ## fact.named
            Named ({who}): {number}
            """,
    });

    public IGame Create(GameSetup setup) => new SecretStartGame(setup);

    public IReadOnlyList<Arena.Debriefing.DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];
}

public sealed class SecretStartGame(GameSetup setup) : IGame
{
    private readonly SeededRng _rng = new(setup.Seed);

    public GameResult? Result { get; private set; }

    public IReadOnlyList<TurnRequest> PendingTurns => Result is not null
        ? []
        : [.. Seat.All.Select(seat => new TurnRequest($"name-{seat}", "name", seat, FieldRule.Forbidden, FieldRule.Required, TemplateRef.Of("instruction"), new MoveFormat(TemplateRef.Of("format"))))];

    public TurnResolution Start() => new([.. Seat.All.Select(seat =>
    {
        var card = $"CARD-{seat}-{_rng.NextInt(900) + 100}";
        return new GameFact("card", FactAudience.Seat, seat, TemplateRef.Of("fact.card", ("card", card)), new Dictionary<string, string> { ["card"] = card });
    })]);

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules");

    public TemplateRef ViewFor(Seat seat) => TemplateRef.Of("view");

    public MoveCheck Check(TurnRequest turn, string move) =>
        int.TryParse(move, out var n) && n is >= 1 and <= 9 ? MoveCheck.Ok(n.ToString(System.Globalization.CultureInfo.InvariantCulture)) : MoveCheck.Reject("range", TemplateRef.Of("error.move", ("move", move)));

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        Result = new GameResult("done", new Dictionary<Seat, SeatResult> { [Seat.A] = new(0, "done"), [Seat.B] = new(0, "done") });
        return new TurnResolution([.. submissions.Select(s => new GameFact("named", FactAudience.Everyone, s.Seat, TemplateRef.Of("fact.named", ("number", s.Move ?? "-"))))]);
    }

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => (rng.NextInt(9) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ThrowingGameDefinition : IGameDefinition
{
    public string Id => "throwing";

    public string DisplayName => "Throwing";

    public ITemplateSource Templates { get; } = new SecretStartDefinition().Templates;

    public IGame Create(GameSetup setup) => new ThrowingGame(new SecretStartGame(setup));

    public IReadOnlyList<Arena.Debriefing.DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];

    private sealed class ThrowingGame(SecretStartGame inner) : IGame
    {
        public GameResult? Result => inner.Result;

        public IReadOnlyList<TurnRequest> PendingTurns => inner.PendingTurns;

        public TemplateRef RulesFor(Seat seat) => inner.RulesFor(seat);

        public TemplateRef ViewFor(Seat seat) => inner.ViewFor(seat);

        public MoveCheck Check(TurnRequest turn, string move) => inner.Check(turn, move);

        public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions) => throw new InvalidOperationException("bug in the game");

        public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => inner.SampleLegalMove(turn, rng);
    }
}
