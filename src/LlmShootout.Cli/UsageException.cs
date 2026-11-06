namespace LlmShootout.Cli;

/// <summary>The command line was used wrongly. The message tells the person what to do instead; no stack trace is shown.</summary>
public sealed class UsageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UsageException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public UsageException()
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
