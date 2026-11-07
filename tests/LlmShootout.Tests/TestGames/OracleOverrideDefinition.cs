using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;

namespace LlmShootout.Tests.TestGames;

/// <summary>
/// The take-away game with an oracle whose answers the test dictates. It reaches positions that the real oracle never reports
/// (drawn, unknown), so that the figures and reports for them can be tested.
/// </summary>
public sealed class OracleOverrideDefinition(Func<TurnRequest, PositionAssessment> assess) : IGameDefinition
{
    private readonly TakeAwayDefinition _inner = new();

    public string Id => _inner.Id;

    public string DisplayName => _inner.DisplayName;

    public ITemplateSource Templates => _inner.Templates;

    public IGame Create(GameSetup setup) => new Game(_inner.Create(setup), assess);

    public IReadOnlyList<DebriefingQuestion> ExtraQuestions(GameSetup setup) => _inner.ExtraQuestions(setup);

    private sealed class Game(IGame inner, Func<TurnRequest, PositionAssessment> assess) : IGame, IGameOracle
    {
        public IReadOnlyList<TurnRequest> PendingTurns => inner.PendingTurns;

        public GameResult? Result => inner.Result;

        public TemplateRef RulesFor(Seat seat) => inner.RulesFor(seat);

        public TemplateRef ViewFor(Seat seat) => inner.ViewFor(seat);

        public MoveCheck Check(TurnRequest turn, string move) => inner.Check(turn, move);

        public TurnResolution Apply(IReadOnlyList<TurnSubmission> submissions) => inner.Apply(submissions);

        public string? SampleLegalMove(TurnRequest turn, SeededRng rng) => inner.SampleLegalMove(turn, rng);

        public PositionAssessment Assess(TurnRequest turn) => assess(turn);

        public string ChooseMove(TurnRequest turn, PositionAssessment assessment, SeededRng rng) => ((IGameOracle)inner).ChooseMove(turn, assessment, rng);
    }
}
