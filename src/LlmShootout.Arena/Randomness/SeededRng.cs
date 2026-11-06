// Adapted from AiBehavior.PublicGoods (MIT, same author).
using System.Globalization;

namespace LlmShootout.Arena.Randomness;

/// <summary>
/// Deterministic pseudo-random generator (splitmix64). Its whole state is one 64-bit value,
/// so a match can be replayed bit-identically from its seed.
/// </summary>
public sealed class SeededRng
{
    private ulong _state;

    /// <summary>Creates a generator from a seed.</summary>
    public SeededRng(long seed) => _state = unchecked((ulong)seed);

    private SeededRng(ulong state) => _state = state;

    /// <summary>Advances the generator and returns the next 64-bit value.</summary>
    public ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Returns a double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Returns an integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextDouble() * maxExclusive);
    }

    /// <summary>Returns true with probability <paramref name="p"/> (clamped to [0, 1]).</summary>
    public bool NextBool(double p) => NextDouble() < Math.Clamp(p, 0.0, 1.0);

    /// <summary>Serializes the full generator state.</summary>
    public string Serialize() => _state.ToString(CultureInfo.InvariantCulture);

    /// <summary>Restores a generator from a state produced by <see cref="Serialize"/>.</summary>
    public static SeededRng Deserialize(string state) =>
        new(ulong.Parse(state, CultureInfo.InvariantCulture));
}
