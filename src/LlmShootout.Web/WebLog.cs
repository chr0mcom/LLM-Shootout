namespace LlmShootout.Web;

internal static partial class WebLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Model list of endpoint '{Endpoint}' is not available: {Reason}")]
    public static partial void ModelListUnavailable(ILogger logger, string endpoint, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Match {Run} failed unexpectedly")]
    public static partial void RunFailed(ILogger logger, Exception exception, string run);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The report of match {Run} could not be written")]
    public static partial void ReportFailed(ILogger logger, Exception exception, string run);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The cost of match {Run} could not be booked against the daily limit")]
    public static partial void CostBookingFailed(ILogger logger, Exception exception, string run);
}
