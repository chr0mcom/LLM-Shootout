namespace LlmShootout.Web.Runs;

/// <summary>What the browser sends to start a match.</summary>
internal sealed record StartRequest(
    string Game,
    string A,
    string B,
    string? Language = null,
    bool? Communication = null,
    string? FirstSeat = null,
    long? Seed = null,
    decimal? MaxCostUsd = null,
    bool? Debrief = null);

/// <summary>The start request could not be turned into a match; the message is meant for the person in the browser.</summary>
internal sealed class StartRejectedException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int StatusCode => statusCode;
}
