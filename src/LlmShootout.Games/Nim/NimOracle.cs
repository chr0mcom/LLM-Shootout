using System.Collections.Concurrent;

namespace LlmShootout.Games.Nim;

/// <summary>
/// Exact solution of Nim by minimax with memoization, for every variant: normal play or misère, with or without a limit
/// per move. A position is won for the player to move if one regular move leads to a position that is lost for the opponent.
/// When no stone is left, the player to move has lost in normal play (the opponent took the last stone) and has won in the
/// misère variant. The oracle rates regular play only: <c>pass</c>, <c>resign</c> and the move limit are not part of it.
/// </summary>
/// <remarks>
/// The value of a position does not depend on the order of the heaps, so the memo is keyed by the sorted heaps, four bits per
/// heap (a heap holds at most <see cref="NimOptions.MaxStones"/> = 15 stones). The key of more than <see cref="NimOptions.MaxHeaps"/> heaps could
/// collide, so the public methods refuse such positions. There are at most 15,504 positions with five heaps.
/// </remarks>
public sealed class NimOracle
{
    private const int BitsPerHeap = 4;

    private static readonly ConcurrentDictionary<(bool Misere, int? MaxTake), NimOracle> Shared = new();

    private readonly bool _misere;
    private readonly int? _maxTake;
    private readonly ConcurrentDictionary<int, bool> _memo = [];

    private NimOracle(bool misere, int? maxTake)
    {
        _misere = misere;
        _maxTake = maxTake;
    }

    /// <summary>The oracle of a variant. Oracles are shared, because the memo only depends on the two options.</summary>
    /// <param name="misere">True if whoever takes the last stone loses.</param>
    /// <param name="maxTake">The most stones one move may take, or null for no limit.</param>
    public static NimOracle For(bool misere, int? maxTake) => Shared.GetOrAdd((misere, maxTake), key => new NimOracle(key.Misere, key.MaxTake));

    /// <summary>True if the player to move can force a win from these heaps.</summary>
    /// <exception cref="ArgumentException">There are more than <see cref="NimOptions.MaxHeaps"/> heaps, or a heap has less than 0 or more than <see cref="NimOptions.MaxStones"/> stones.</exception>
    public bool Wins(IReadOnlyList<int> heaps)
    {
        ValidatePosition(heaps);
        return Solve([.. heaps]);
    }

    /// <summary>
    /// All regular moves that lead to a position that is lost for the opponent, ordered by heap and then by count. The heap is the
    /// real index, also when several heaps have the same size. Empty in a lost position.
    /// </summary>
    /// <exception cref="ArgumentException">There are more than <see cref="NimOptions.MaxHeaps"/> heaps, or a heap has less than 0 or more than <see cref="NimOptions.MaxStones"/> stones.</exception>
    public IReadOnlyList<NimMove> WinningMoves(IReadOnlyList<int> heaps)
    {
        ValidatePosition(heaps);
        return [.. NimMove.Legal(heaps, _maxTake).Where(move => !Solve(After(heaps, move)))];
    }

    /// <summary>
    /// The move for a lost position: one stone from the largest heap, and from the one with the smallest number if several are
    /// equally large. It is the same in every lost position, so nobody can mistake it for a calculated move.
    /// </summary>
    public static NimMove LosingMove(IReadOnlyList<int> heaps)
    {
        ArgumentNullException.ThrowIfNull(heaps);
        var largest = heaps.Max();
        return new NimMove(heaps.ToList().IndexOf(largest) + 1, 1);
    }

    private static void ValidatePosition(IReadOnlyList<int> heaps)
    {
        ArgumentNullException.ThrowIfNull(heaps);
        if (heaps.Count > NimOptions.MaxHeaps)
        {
            throw new ArgumentException($"The oracle solves positions of at most {NimOptions.MaxHeaps} heaps, but got {heaps.Count}.", nameof(heaps));
        }

        if (heaps.Any(stones => stones is < 0 or > NimOptions.MaxStones))
        {
            throw new ArgumentException($"A heap must hold 0 to {NimOptions.MaxStones} stones.", nameof(heaps));
        }
    }

    private bool Solve(int[] heaps)
    {
        var key = Key(heaps);
        if (_memo.TryGetValue(key, out var known))
        {
            return known;
        }

        var wins = heaps.All(stones => stones == 0)
            ? _misere
            : NimMove.Legal(heaps, _maxTake).Any(move => !Solve(After(heaps, move)));
        _memo[key] = wins;
        return wins;
    }

    private static int[] After(IReadOnlyList<int> heaps, NimMove move)
    {
        var after = new int[heaps.Count];
        for (var i = 0; i < after.Length; i++)
        {
            after[i] = heaps[i];
        }

        after[move.Heap - 1] -= move.Count;
        return after;
    }

    private static int Key(int[] heaps) =>
        heaps.Where(stones => stones > 0).Order().Aggregate(0, (key, stones) => (key << BitsPerHeap) | stones);
}
