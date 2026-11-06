using LlmShootout.Arena.Randomness;

namespace LlmShootout.Tests;

public class SeededRngTests
{
    [Fact]
    public void Same_seed_yields_same_sequence()
    {
        var a = new SeededRng(42);
        var b = new SeededRng(42);
        Assert.Equal(Enumerable.Range(0, 5).Select(_ => a.NextUInt64()), Enumerable.Range(0, 5).Select(_ => b.NextUInt64()));
    }

    [Fact]
    public void Serialized_state_resumes_bit_identically()
    {
        var rng = new SeededRng(7);
        rng.NextInt(10);
        var resumed = SeededRng.Deserialize(rng.Serialize());
        Assert.Equal(rng.NextUInt64(), resumed.NextUInt64());
    }
}
