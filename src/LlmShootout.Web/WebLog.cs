namespace LlmShootout.Web;

internal static partial class WebLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Model list of endpoint '{Endpoint}' is not available: {Reason}")]
    public static partial void ModelListUnavailable(ILogger logger, string endpoint, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Match {Run} failed unexpectedly")]
    public static partial void RunFailed(ILogger logger, Exception exception, string run);
}
