using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Randomness;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Tests.TestGames;

/// <summary>Test game: alternating guesses of a hidden number with higher/lower feedback.</summary>
public sealed class NumberGuessDefinition : IGameDefinition
{
    public string Id => "number-guess";

    public string DisplayName => "Number guess";

    public ITemplateSource Templates { get; } = MarkdownTemplates.FromText(new Dictionary<string, string>
    {
        ["en"] = """
            ## rules
            You and your opponent take turns guessing a secret number from 1 to 100. Whoever hits it wins. After {max} guesses it is a draw.
            ## view
            Secret number: between 1 and 100. Next is guess {n} of {max}.
            ## instruction
            Guess the number.
            ## format
            An integer from 1 to 100.
            ## error.not_number
            "{move}" is not an integer
            ## error.range
            {move} is outside 1 to 100
            ## fact.guess
            {who} guessed {guess}: {hint}.
            ## metric.guesses
            Guesses
            ## metric.hit_rate
            Hits per guess
            ## metric.never
            Never measurable
            ## estimate_meaning
            Your estimated chance of guessing correctly with this guess, in percent.
            """,
        ["de"] = """
            ## rules
            Du und dein Gegner ratet abwechselnd eine geheime Zahl von 1 bis 100. Wer sie trifft, gewinnt. Nach {max} Tipps ist es unentschieden.
            ## view
            Geheime Zahl: zwischen 1 und 100. Als Nächstes kommt Tipp {n} von {max}.
            ## instruction
            Rate die Zahl.
            ## format
            Eine ganze Zahl von 1 bis 100.
            ## error.not_number
            "{move}" ist keine ganze Zahl
            ## error.range
            {move} liegt außerhalb von 1 bis 100
            ## fact.guess
            {who} hat {guess} geraten: {hint}.
            ## metric.guesses
            Tipps
            ## metric.hit_rate
            Treffer je Tipp
            ## metric.never
            Nie messbar
            ## estimate_meaning
            Deine geschätzte Trefferchance mit diesem Tipp in Prozent.
            """,
    });

    public IGame Create(GameSetup setup) => new NumberGuessGame(setup);

    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => [];

    public IGameAnalyzer? Analyzer { get; } = new NumberGuessAnalyzer();
}

/// <summary>Test analyzer: declares one sum, one ratio and one ratio without a denominator for the series report.</summary>
public sealed class NumberGuessAnalyzer : IGameAnalyzer
{
    public IReadOnlyList<SeriesMetric> SeriesMetrics { get; } =
    [
        new("guesses", TemplateRef.Of("metric.guesses"), MetricKind.Sum, (match, seat) => new Fraction(match.Turns(seat).Count())),
        new("hit-rate", TemplateRef.Of("metric.hit_rate"), MetricKind.Ratio, (match, seat) => match.Result is { } result ? new Fraction(result.Seats[seat].Score, match.Turns(seat).Count()) : null),
        new("never", TemplateRef.Of("metric.never"), MetricKind.Ratio, (_, _) => new Fraction(0, 0)),
    ];

    public IReadOnlyList<ReportSection> AnalyzeMatch(MatchRecord match) => [];
}

public sealed class NumberGuessGame(GameSetup setup) : IGame
{
    private const int MaxGuesses = 6;
    private readonly int _secret = new SeededRng(setup.Seed).NextInt(100) + 1;
    private Seat _current = setup.FirstSeat;
    private int _guess = 1;

    public int Secret => _secret;

    public GameResult? Result { get; private set; }

    private FieldRule EstimateRule => setup.Options?["estimate"]?.GetValue<string>() switch
    {
        "required" => FieldRule.Required,
        "optional" => FieldRule.Optional,
        _ => FieldRule.Forbidden,
    };

    private TemplateRef? EstimateMeaning =>
        EstimateRule == FieldRule.Forbidden || setup.Options?["withoutMeaning"]?.GetValue<bool>() == true ? null : TemplateRef.Of("estimate_meaning");

    private FieldRule MessageRule =>
        !setup.Communication ? FieldRule.Forbidden : setup.Options?["messageRequired"]?.GetValue<bool>() == true ? FieldRule.Required : FieldRule.Optional;

    public IReadOnlyList<TurnRequest> PendingTurns => Result is not null
        ? []
        :
        [
            new TurnRequest(
                $"guess-{_guess}",
                "guess",
                _current,
                MessageRule,
                FieldRule.Required,
                TemplateRef.Of("instruction"),
                new MoveFormat(TemplateRef.Of("format")),
                Estimate: EstimateRule,
                EstimateMeaning: EstimateMeaning),
        ];

    public TemplateRef RulesFor(Seat seat) => TemplateRef.Of("rules", ("max", MaxGuesses.ToString(CultureInfo.InvariantCulture)));

    public TemplateRef ViewFor(Seat seat) => TemplateRef.Of("view", ("n", _guess.ToString(CultureInfo.InvariantCulture)), ("max", MaxGuesses.ToString(CultureInfo.InvariantCulture)));

    public MoveCheck Check(TurnRequest turn, string move)
    {
        if (!int.TryParse(move, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return MoveCheck.Reject("not-number", TemplateRef.Of("error.not_number", ("move", move)));
        }

        return number is >= 1 and <= 100
            ? MoveCheck.Ok(number.ToString(CultureInfo.InvariantCulture))
            : MoveCheck.Reject("range", TemplateRef.Of("error.range", ("move", move)));
    }

    public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions)
    {
        var submission = Assert.Single(submissions);
        var facts = new List<GameFact>();
        if (submission.Move is { } move)
        {
            var guess = int.Parse(move, CultureInfo.InvariantCulture);
            var hint = guess == _secret ? "correct" : guess < _secret ? "higher" : "lower";
            facts.Add(new GameFact("guess", FactAudience.Everyone, submission.Seat, TemplateRef.Of("fact.guess", ("guess", move), ("hint", hint)), new Dictionary<string, string> { ["guess"] = move, ["hint"] = hint }));
            if (guess == _secret)
            {
                Result = new GameResult("solved", new Dictionary<Seat, SeatResult> { [submission.Seat] = new(1, "win"), [submission.Seat.Opponent] = new(0, "loss") });
            }
        }

        if (Result is null && ++_guess > MaxGuesses)
        {
            Result = new GameResult("draw", new Dictionary<Seat, SeatResult> { [Seat.A] = new(0, "draw"), [Seat.B] = new(0, "draw") });
        }

        _current = _current.Opponent;
        return new TurnResolution(facts);
    }

    public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => (rng.NextInt(100) + 1).ToString(CultureInfo.InvariantCulture);
}
