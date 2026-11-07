using System.Globalization;

namespace LlmShootout.Games.Nim;

/// <summary>A regular move of Nim: take <see cref="Count"/> stones from heap <see cref="Heap"/> (heaps are numbered from 1).</summary>
/// <param name="Heap">The heap, counted from 1.</param>
/// <param name="Count">The number of stones to take.</param>
public readonly record struct NimMove(int Heap, int Count)
{
    private const int MaxDigits = 9;

    /// <summary>The canonical text of the move, <c>heap:count</c>, for example <c>2:3</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Heap}:{Count}");

    /// <summary>Reads a move. Spaces around the numbers and around the colon are fine; anything else that is not <c>heap:count</c> is not.</summary>
    public static bool TryParse(string? text, out NimMove move)
    {
        move = default;
        var parts = text?.Split(':');
        if (parts is not [var heap, var count] || !TryNumber(heap, out var h) || !TryNumber(count, out var n))
        {
            return false;
        }

        move = new NimMove(h, n);
        return true;
    }

    /// <summary>Reads a move in its canonical text. Throws <see cref="FormatException"/> for anything else.</summary>
    public static NimMove Parse(string text) =>
        TryParse(text, out var move) ? move : throw new FormatException($"'{text}' is not a Nim move of the form heap:count.");

    /// <summary>
    /// All regular moves of a position, ordered by heap and then by count: at least one stone from one heap, at most
    /// <paramref name="maxTake"/> if there is a limit. The game and its oracle share this one list.
    /// </summary>
    public static IReadOnlyList<NimMove> Legal(IReadOnlyList<int> heaps, int? maxTake)
    {
        ArgumentNullException.ThrowIfNull(heaps);
        return [.. heaps.SelectMany((stones, index) => Enumerable.Range(1, Math.Min(stones, maxTake ?? stones)).Select(count => new NimMove(index + 1, count)))];
    }

    private static bool TryNumber(string text, out int number)
    {
        var trimmed = text.Trim();
        number = 0;
        return trimmed.Length is > 0 and <= MaxDigits
            && trimmed.All(char.IsAsciiDigit)
            && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
