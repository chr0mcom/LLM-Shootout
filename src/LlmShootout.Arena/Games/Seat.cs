namespace LlmShootout.Arena.Games;

/// <summary>A place at the table. Every player occupies exactly one seat for the whole match.</summary>
public enum Seat
{
    /// <summary>The first seat.</summary>
    A,

    /// <summary>The second seat.</summary>
    B,
}

/// <summary>Extension members for <see cref="Seat"/> (C# 14 extension blocks).</summary>
public static class SeatExtensions
{
    private static readonly Seat[] AllSeats = [Seat.A, Seat.B];

    extension(Seat seat)
    {
        /// <summary>The other seat. Only defined for two-seat games; the arena iterates <c>Seat.All</c> instead.</summary>
        public Seat Opponent => seat == Seat.A ? Seat.B : Seat.A;
    }

    extension(Seat)
    {
        /// <summary>All seats of a match, in play order.</summary>
        public static IReadOnlyList<Seat> All => AllSeats;
    }
}
