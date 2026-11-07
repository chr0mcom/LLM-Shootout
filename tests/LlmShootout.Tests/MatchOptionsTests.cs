using LlmShootout.Arena;

namespace LlmShootout.Tests;

public sealed class MatchOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Attempts_and_word_limits_must_be_positive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxAttempts = value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxMessageWords = value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { MaxThoughtWords = value });
    }

    [Fact]
    public void Thought_memory_may_be_zero_but_not_negative()
    {
        Assert.Equal(0, new MatchOptions { ThoughtMemory = 0 }.ThoughtMemory);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOptions { ThoughtMemory = -1 });
    }

    [Fact]
    public void Defaults_match_the_specification()
    {
        var options = new MatchOptions();

        Assert.Equal((3, 40, 20, 10, 1m), (options.MaxAttempts, options.MaxThoughtWords, options.MaxMessageWords, options.ThoughtMemory, options.MaxCostUsd));
    }
}
