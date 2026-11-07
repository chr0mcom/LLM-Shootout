using LlmShootout.Arena.Games;

namespace LlmShootout.Tests.Games;

public sealed class SeatTests
{
    [Fact]
    public void Seat_extension_members_work()
    {
        Assert.Equal(Seat.B, Seat.A.Opponent);
        Assert.Equal(Seat.A, Seat.B.Opponent);
        Assert.Equal([Seat.A, Seat.B], Seat.All);
    }
}
