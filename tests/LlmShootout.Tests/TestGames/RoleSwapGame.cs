using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>Test game: three rounds of offer and response, with roles swapping every round, a private note and an analysis fact.</summary>
public sealed class RoleSwapDefinition : IGameDefinition
{
    public string Id => "role-swap";

    public string DisplayName => "Role swap";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            Each round has 10 points. The proposer offers the other player a share, the responder accepts or rejects. Rejection gives both 0. Roles swap every round.
            ## view.offer
            Round {round} of 3. You are the proposer.
            ## view.respond
            Round {round} of 3. You are the responder.
            ## instruction.offer
            Offer 0 to 10 points to the other player.
            ## instruction.respond
            Accept or reject the offer.
            ## format.offer
            An integer from 0 to 10.
            ## format.respond
            accept or reject.
            ## error.offer
            "{move}" is not an integer from 0 to 10
            ## error.respond
            "{move}" is neither accept nor reject
            ## fact.offer
            {who} offered {amount} points.
            ## fact.note
            Your private minimum is {minimum} points.
            ## fact.response
            {who} chose to {answer}.
            ## fact.analysis
            Offer fair: {fair}.
            """,
    });

    public IGame Create(GameSetup setup) => new RoleSwapGame(setup);

    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];

    public IGameAnalyzer? Analyzer => null;
}

public sealed class RoleSwapGame(GameSetup setup) : IGame
{
    private readonly int _minimum = new SeededRng(setup.Seed).NextInt(5) + 1;
    private readonly double[] _scores = new double[2];
    private int _round = 1;
    private int _offer = -1;

    public GameResult? Result { get; private set; }

    private Seat Proposer => _round % 2 == 1 ? setup.FirstSeat : setup.FirstSeat.Opponent;

    public IReadOnlyList<TurnRequest> PendingTurns
    {
        get
        {
            if (Result is not null)
            {
                return [];
            }

            var offering = _offer < 0;
            var kind = offering ? "offer" : "respond";
            var seat = offering ? Proposer : Proposer.Opponent;
            return
            [
                new TurnRequest(
                    $"{kind}-{_round}",
                    kind,
                    seat,
                    setup.Communication ? FieldRule.Optional : FieldRule.Forbidden,
                    FieldRule.Required,
                    TemplateRef.Of($"instruction.{kind}"),
                    new MoveFormat(TemplateRef.Of($"format.{kind}"), offering ? null : ["accept", "reject"])),
            ];
        }
    }

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules");

    public TemplateRef ViewFor(Seat seat) =>
        TemplateRef.Of(seat == Proposer && _offer < 0 ? "view.offer" : "view.respond", ("round", _round.ToString(CultureInfo.InvariantCulture)));

    public MoveCheck Check(TurnRequest turn, string move)
    {
        if (turn.Kind == "offer")
        {
            return int.TryParse(move, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) && amount is >= 0 and <= 10
                ? MoveCheck.Ok(amount.ToString(CultureInfo.InvariantCulture))
                : MoveCheck.Reject("offer", TemplateRef.Of("error.offer", ("move", move)), "0 to 10");
        }

        var normalized = move.Trim().ToLowerInvariant();
        return normalized is "accept" or "reject"
            ? MoveCheck.Ok(normalized)
            : MoveCheck.Reject("respond", TemplateRef.Of("error.respond", ("move", move)), "\"accept\", \"reject\"");
    }

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        var submission = Assert.Single(submissions);
        return submission.Seat == Proposer && _offer < 0 ? ApplyOffer(submission) : ApplyResponse(submission);
    }

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) =>
        turn.Kind == "offer" ? rng.NextInt(11).ToString(CultureInfo.InvariantCulture) : rng.NextBool(0.5) ? "accept" : "reject";

    private TurnResolution ApplyOffer(TurnSubmission submission)
    {
        _offer = submission.Move is null ? 0 : int.Parse(submission.Move, CultureInfo.InvariantCulture);
        var amount = _offer.ToString(CultureInfo.InvariantCulture);
        return new TurnResolution(
        [
            new GameFact("offer", FactAudience.Everyone, submission.Seat, TemplateRef.Of("fact.offer", ("amount", amount))),
            new GameFact("note", FactAudience.Seat, submission.Seat.Opponent, TemplateRef.Of("fact.note", ("minimum", _minimum.ToString(CultureInfo.InvariantCulture)))),
            new GameFact("analysis", FactAudience.Analysis, submission.Seat, TemplateRef.Of("fact.analysis", ("fair", (_offer >= 4).ToString())), new Dictionary<string, string> { ["fair"] = (_offer >= 4).ToString() }),
        ]);
    }

    private TurnResolution ApplyResponse(TurnSubmission submission)
    {
        var accepted = submission.Move == "accept";
        if (accepted)
        {
            _scores[(int)Proposer] += 10 - _offer;
            _scores[(int)Proposer.Opponent] += _offer;
        }

        var fact = new GameFact("response", FactAudience.Everyone, submission.Seat, TemplateRef.Of("fact.response", ("answer", accepted ? "accept" : "reject")));
        _offer = -1;
        if (++_round > 3)
        {
            Result = new GameResult(
                "finished",
                new Dictionary<Seat, SeatResult> { [Seat.A] = new(_scores[0], "score"), [Seat.B] = new(_scores[1], "score") });
        }

        return new TurnResolution([fact]);
    }
}
