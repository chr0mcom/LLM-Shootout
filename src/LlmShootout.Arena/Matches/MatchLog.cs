using Microsoft.Extensions.Logging;

namespace LlmShootout.Arena.Matches;

internal static partial class MatchLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Match started: {GameId}, seed {Seed}, directory {Directory}")]
    public static partial void Started(ILogger logger, string gameId, long seed, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Turn {TurnId}, seat {Seat}, attempt {Attempt}")]
    public static partial void Attempt(ILogger logger, string turnId, Games.Seat seat, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Turn {TurnId}, seat {Seat}, attempt {Attempt}: {Class}")]
    public static partial void Classified(ILogger logger, string turnId, Games.Seat seat, int attempt, Answers.AnswerClass @class);

    [LoggerMessage(Level = LogLevel.Information, Message = "Match finished: {Outcome}")]
    public static partial void Finished(ILogger logger, string outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Match aborted: {Reason}: {Detail}")]
    public static partial void Aborted(ILogger logger, string reason, string detail);
}
