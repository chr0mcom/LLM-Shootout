using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;

namespace LlmShootout.Tests.Prompting;

public sealed class TimelineTests
{
    [Fact]
    public void Timeline_shows_each_seat_only_what_it_may_see()
    {
        var timeline = new Timeline();
        timeline.AddMessage("t1", Seat.A, "hi");
        timeline.AddFact("t1", new GameFact("pub", FactAudience.Everyone, Seat.A, new TemplateRef("x")));
        timeline.AddFact("t1", new GameFact("priv", FactAudience.Seat, Seat.B, new TemplateRef("y")));
        timeline.AddFact("t1", new GameFact("ana", FactAudience.Analysis, Seat.A, new TemplateRef("z")));

        Assert.Equal(["pub", "priv"], timeline.VisibleTo(Seat.B).OfType<FactEntry>().Select(f => f.Fact.Kind));
        Assert.Equal(["pub"], timeline.VisibleTo(Seat.A).OfType<FactEntry>().Select(f => f.Fact.Kind));
        Assert.Equal(4, timeline.Entries.Count);
    }

    [Fact]
    public void Thought_log_returns_only_own_recent_thoughts()
    {
        var log = new ThoughtLog();
        log.Add(Seat.A, "a1");
        log.Add(Seat.B, "b1");
        log.Add(Seat.A, "a2");
        log.Add(Seat.A, "a3");

        Assert.Equal(["a2", "a3"], log.Recent(Seat.A, 2));
        Assert.Empty(log.Recent(Seat.A, 0));
        Assert.Equal(["b1"], log.Recent(Seat.B, 10));
    }
}
