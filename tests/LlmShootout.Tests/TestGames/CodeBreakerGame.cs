using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>Test game: three rounds in which one player sets a number from 1 to 9 and the other guesses it; the roles swap every round. The setter gets a private note and every guess an analysis fact.</summary>
public sealed class CodeBreakerDefinition : IGameDefinition
{
    public string Id => "code-breaker";

    public string DisplayName => "Code breaker";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            Each round, one player is the setter and sets a number from 1 to 9. The other player is the guesser and guesses it. A correct guess scores 1 point. The roles swap every round.
            ## view.set
            Round {round} of 3. You are the setter.
            ## view.guess
            Round {round} of 3. You are the guesser.
            ## instruction.set
            Set a number from 1 to 9.
            ## instruction.guess
            Guess the number.
            ## format.set
            An integer from 1 to 9.
            ## format.guess
            An integer from 1 to 9.
            ## error.set
            "{move}" is not an integer from 1 to 9
            ## error.guess
            "{move}" is not an integer from 1 to 9
            ## fact.set
            {who} has set a number.
            ## fact.note
            Your number is {code}.
            ## fact.guess
            {who} guessed {guess}.
            ## fact.analysis
            Guess within 1: {close}.
            """,
    });

    public IGame Create(GameSetup setup) => new CodeBreakerGame(setup);

    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];

    public IGameAnalyzer? Analyzer => null;
}

public sealed class CodeBreakerGame(GameSetup setup) : IGame
{
    private const int Rounds = 3;

    private readonly double[] _scores = new double[2];
    private int _round = 1;
    private int _code;

    public GameResult? Result { get; private set; }

    private bool Setting => _code == 0;

    private Seat Setter => _round % 2 == 1 ? setup.FirstSeat : setup.FirstSeat.Opponent;

    public IReadOnlyList<TurnRequest> PendingTurns
    {
        get
        {
            if (Result is not null)
            {
                return [];
            }

            var kind = Setting ? "set" : "guess";
            var seat = Setting ? Setter : Setter.Opponent;
            return
            [
                new TurnRequest(
                    $"{kind}-{_round}",
                    kind,
                    seat,
                    setup.Communication ? FieldRule.Optional : FieldRule.Forbidden,
                    FieldRule.Required,
                    TemplateRef.Of($"instruction.{kind}"),
                    new MoveFormat(TemplateRef.Of($"format.{kind}"), null)),
            ];
        }
    }

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules");

    public TemplateRef ViewFor(Seat seat) =>
        TemplateRef.Of(seat == Setter && Setting ? "view.set" : "view.guess", ("round", _round.ToString(CultureInfo.InvariantCulture)));

    public MoveCheck Check(TurnRequest turn, string move) =>
        int.TryParse(move, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 9
            ? MoveCheck.Ok(number.ToString(CultureInfo.InvariantCulture))
            : MoveCheck.Reject(turn.Kind, TemplateRef.Of($"error.{turn.Kind}", ("move", move)), "1 to 9");

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        var submission = Assert.Single(submissions);
        return Setting ? ApplySet(submission) : ApplyGuess(submission);
    }

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) =>
        (rng.NextInt(9) + 1).ToString(CultureInfo.InvariantCulture);

    private TurnResolution ApplySet(TurnSubmission submission)
    {
        _code = submission.Move is null ? 1 : int.Parse(submission.Move, CultureInfo.InvariantCulture);
        return new TurnResolution(
        [
            new GameFact("set", FactAudience.Everyone, submission.Seat, TemplateRef.Of("fact.set")),
            new GameFact("note", FactAudience.Seat, submission.Seat, TemplateRef.Of("fact.note", ("code", _code.ToString(CultureInfo.InvariantCulture)))),
        ]);
    }

    private TurnResolution ApplyGuess(TurnSubmission submission)
    {
        var guess = submission.Move is null ? 1 : int.Parse(submission.Move, CultureInfo.InvariantCulture);
        var close = (Math.Abs(guess - _code) <= 1).ToString();
        if (guess == _code)
        {
            _scores[(int)submission.Seat] += 1;
        }

        var facts = new[]
        {
            new GameFact("guess", FactAudience.Everyone, submission.Seat, TemplateRef.Of("fact.guess", ("guess", guess.ToString(CultureInfo.InvariantCulture)))),
            new GameFact("analysis", FactAudience.Analysis, submission.Seat, TemplateRef.Of("fact.analysis", ("close", close)), new Dictionary<string, string> { ["close"] = close }),
        };
        _code = 0;
        if (++_round > Rounds)
        {
            Result = new GameResult(
                "finished",
                new Dictionary<Seat, SeatResult> { [Seat.A] = new(_scores[0], "score"), [Seat.B] = new(_scores[1], "score") });
        }

        return new TurnResolution(facts);
    }
}
