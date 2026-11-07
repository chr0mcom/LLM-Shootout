using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Games;

public sealed class NimMoveTests
{
    [Theory]
    [InlineData("2:3", 2, 3)]
    [InlineData(" 2 : 3 ", 2, 3)]
    [InlineData("02:03", 2, 3)]
    [InlineData("1:15", 1, 15)]
    [InlineData("0:0", 0, 0)]
    public void A_move_is_read_leniently(string text, int heap, int count)
    {
        Assert.True(NimMove.TryParse(text, out var move));
        Assert.Equal(new NimMove(heap, count), move);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("2:")]
    [InlineData(":3")]
    [InlineData("2:3:4")]
    [InlineData("2-3")]
    [InlineData("-2:3")]
    [InlineData("2:-3")]
    [InlineData("+2:3")]
    [InlineData("2.0:3")]
    [InlineData("a:b")]
    [InlineData("1 2:3")]
    [InlineData("1234567890:1")]
    [InlineData("1:٣")]
    public void Anything_else_is_not_a_move(string? text)
    {
        Assert.False(NimMove.TryParse(text, out var move));
        Assert.Equal(default, move);
    }

    [Fact]
    public void A_move_is_written_as_heap_colon_count()
    {
        Assert.Equal("2:3", new NimMove(2, 3).ToString());
        Assert.Equal(new NimMove(5, 1), NimMove.Parse(new NimMove(5, 1).ToString()));
        Assert.Throws<FormatException>(() => NimMove.Parse("pass"));
    }

    [Fact]
    public void The_legal_moves_are_ordered_by_heap_and_count_and_skip_empty_heaps()
    {
        var moves = NimMove.Legal([2, 0, 3], null);

        Assert.Equal(["1:1", "1:2", "3:1", "3:2", "3:3"], moves.Select(m => m.ToString()));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(4, 11)]
    [InlineData(15, 12)]
    public void A_limit_per_move_cuts_every_heap(int maxTake, int expected)
    {
        Assert.Equal(expected, NimMove.Legal([3, 4, 5], maxTake).Count);
        Assert.All(NimMove.Legal([3, 4, 5], maxTake), move => Assert.InRange(move.Count, 1, maxTake));
    }

    [Fact]
    public void No_stones_means_no_move()
    {
        Assert.Empty(NimMove.Legal([0, 0], null));
        Assert.Empty(NimMove.Legal([], 2));
        Assert.Throws<ArgumentNullException>(() => NimMove.Legal(null!, 2));
    }
}
