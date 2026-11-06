using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>Test game: both seats bid secretly and at the same time; the higher bid wins.</summary>
public sealed class SealedBidDefinition : IGameDefinition
{
    public string Id => "sealed-bid";

    public string DisplayName => "Sealed bid";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            Both players bid a number from 0 to 9 at the same time. The higher bid wins, equal bids draw.
            ## view
            No bids yet.
            ## instruction
            Place your bid.
            ## format
            An integer from 0 to 9.
            ## error.bid
            "{move}" is not an integer from 0 to 9
            ## fact.bid
            {who} bid {bid}.
            """,
    });

    public SealedBidGame? Created { get; private set; }

    public IGame Create(GameSetup setup) => Created = new SealedBidGame(setup);

    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];

    public IGameAnalyzer? Analyzer => null;
}

public sealed class SealedBidGame(GameSetup setup) : IGame
{
    public GameResult? Result { get; private set; }

    public List<IReadOnlyList<TurnSubmission>> AppliedRounds { get; } = [];

    public IReadOnlyList<TurnRequest> PendingTurns => Result is not null
        ? []
        : [.. Seat.All.Select(seat => new TurnRequest(
            $"bid-{seat}",
            "bid",
            seat,
            setup.Communication ? FieldRule.Optional : FieldRule.Forbidden,
            FieldRule.Required,
            TemplateRef.Of("instruction"),
            new MoveFormat(TemplateRef.Of("format"), [.. Enumerable.Range(0, 10).Select(i => i.ToString(CultureInfo.InvariantCulture))])))];

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules");

    public TemplateRef ViewFor(Seat seat) => TemplateRef.Of("view");

    public MoveCheck Check(TurnRequest turn, string move) =>
        int.TryParse(move, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bid) && bid is >= 0 and <= 9
            ? MoveCheck.Ok(bid.ToString(CultureInfo.InvariantCulture))
            : MoveCheck.Reject("bid", TemplateRef.Of("error.bid", ("move", move)), "0 to 9");

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        AppliedRounds.Add(submissions);
        var bids = submissions.ToDictionary(s => s.Seat, s => s.Move is null ? -1 : int.Parse(s.Move, CultureInfo.InvariantCulture));
        var facts = submissions.Select(s => new GameFact("bid", FactAudience.Everyone, s.Seat, TemplateRef.Of("fact.bid", ("bid", bids[s.Seat].ToString(CultureInfo.InvariantCulture))))).ToArray();
        var (a, b) = (bids[Seat.A], bids[Seat.B]);
        Result = new GameResult(
            a == b ? "draw" : a > b ? "a_wins" : "b_wins",
            new Dictionary<Seat, SeatResult> { [Seat.A] = new(a > b ? 1 : 0, a > b ? "win" : "loss"), [Seat.B] = new(b > a ? 1 : 0, b > a ? "win" : "loss") });
        return new TurnResolution(facts);
    }

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => rng.NextInt(10).ToString(CultureInfo.InvariantCulture);
}
