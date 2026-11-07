namespace LlmShootout.Tests.Games;

/// <summary>
/// An independent reference for the oracle of Nim: plain recursion over lists that tries every move, without a memo, without sorting
/// and without any formula. It is slow and meant for small positions only.
/// </summary>
internal static class NimBruteForce
{
    /// <summary>True if the player to move can force a win.</summary>
    public static bool MoverWins(List<int> heaps, bool misere, int limit)
    {
        if (heaps.All(stones => stones == 0))
        {
            return misere;
        }

        for (var heap = 0; heap < heaps.Count; heap++)
        {
            for (var take = 1; take <= Math.Min(heaps[heap], limit); take++)
            {
                if (!MoverWins(After(heaps, heap, take), misere, limit))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>All moves as <c>heap:count</c> (heaps from 1) that leave the opponent in a lost position, ordered by heap and count.</summary>
    public static List<string> WinningMoves(List<int> heaps, bool misere, int limit)
    {
        var moves = new List<string>();
        for (var heap = 0; heap < heaps.Count; heap++)
        {
            for (var take = 1; take <= Math.Min(heaps[heap], limit); take++)
            {
                if (!MoverWins(After(heaps, heap, take), misere, limit))
                {
                    moves.Add($"{heap + 1}:{take}");
                }
            }
        }

        return moves;
    }

    private static List<int> After(List<int> heaps, int heap, int take)
    {
        var copy = new List<int>(heaps);
        copy[heap] -= take;
        return copy;
    }
}
