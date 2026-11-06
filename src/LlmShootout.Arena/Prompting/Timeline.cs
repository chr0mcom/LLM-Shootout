using LlmShootout.Arena.Games;

namespace LlmShootout.Arena.Prompting;

/// <summary>One entry of the shared history of a match.</summary>
public abstract record TimelineEntry(string TurnId);

/// <summary>A public message. A null text means the seat stayed silent.</summary>
public sealed record MessageEntry(string TurnId, Seat Seat, string? Text) : TimelineEntry(TurnId);

/// <summary>A fact reported by the game.</summary>
public sealed record FactEntry(string TurnId, GameFact Fact) : TimelineEntry(TurnId);

/// <summary>
/// The one history of a match: public messages and game facts in order. Prompts, debriefing and reports all read
/// from it, so no game keeps a second history.
/// </summary>
public sealed class Timeline
{
    private readonly List<TimelineEntry> _entries = [];

    /// <summary>All entries, including analysis facts.</summary>
    public IReadOnlyList<TimelineEntry> Entries => _entries;

    /// <summary>Adds a public message.</summary>
    public void AddMessage(string turnId, Seat seat, string? text) => _entries.Add(new MessageEntry(turnId, seat, text));

    /// <summary>Adds a fact.</summary>
    public void AddFact(string turnId, GameFact fact) => _entries.Add(new FactEntry(turnId, fact));

    /// <summary>The entries a seat may see: messages, public facts, and the seat's own private facts.</summary>
    public IEnumerable<TimelineEntry> VisibleTo(Seat seat) =>
        _entries.Where(e => e switch
        {
            MessageEntry => true,
            FactEntry { Fact: { Audience: FactAudience.Everyone } } => true,
            FactEntry { Fact: { Audience: FactAudience.Seat } fact } => fact.Seat == seat,
            _ => false,
        });
}

/// <summary>The private thoughts of the seats, in order.</summary>
public sealed class ThoughtLog
{
    private readonly List<(Seat Seat, string Thought)> _thoughts = [];

    /// <summary>Records the thought of a seat.</summary>
    public void Add(Seat seat, string thought) => _thoughts.Add((seat, thought));

    /// <summary>The most recent <paramref name="count"/> thoughts of a seat, oldest first. Never the opponent's.</summary>
    public IReadOnlyList<string> Recent(Seat seat, int count) =>
        count <= 0 ? [] : [.. _thoughts.Where(t => t.Seat == seat).Select(t => t.Thought).TakeLast(count)];
}
