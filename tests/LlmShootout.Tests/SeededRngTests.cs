using LlmShootout.Arena.Randomness;

namespace LlmShootout.Tests;

public sealed class SeededRngTests
{
    [Fact]
    public void Same_seed_yields_same_sequence()
    {
        var a = new SeededRng(42);
        var b = new SeededRng(42);
        Assert.Equal(Enumerable.Range(0, 5).Select(_ => a.NextUInt64()), Enumerable.Range(0, 5).Select(_ => b.NextUInt64()));
    }
}
