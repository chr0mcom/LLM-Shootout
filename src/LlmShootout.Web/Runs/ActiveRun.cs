using LlmShootout.Arena.Protocol;

namespace LlmShootout.Web.Runs;

/// <summary>
/// One match that runs (or ran last) in this process. It keeps every protocol line, so a browser that connects late,
/// or reconnects, gets the whole match again and then follows it live.
/// </summary>
internal sealed class ActiveRun(string id, string directory, StartRequest request, DateTimeOffset startedAt, CancellationTokenSource cancellation)
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private TaskCompletionSource _changed = NewSignal();

    public string Id => id;

    public string DirectoryPath => directory;

    public string MatchName => Path.GetFileName(directory);

    public StartRequest Request => request;

    public DateTimeOffset StartedAt => startedAt;

    public string Status { get; private set; } = RunStatus.Running;

    public string? Error { get; private set; }

    public bool IsFinished => Status != RunStatus.Running;

    public void Cancel()
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
    }

    public void Append(MatchEvent matchEvent) => Append(ProtocolJson.ToLine(matchEvent));

    public void Append(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
            Signal();
        }
    }

    public void Complete(string status, string? error = null)
    {
        lock (_gate)
        {
            Status = status;
            Error = error;
            Signal();
        }
    }

    /// <summary>The lines from <paramref name="from"/> on, whether the run is over, and a task that completes when something new happens.</summary>
    public (IReadOnlyList<string> Lines, bool Finished, Task Changed) Read(int from)
    {
        lock (_gate)
        {
            return ([.. _lines.Skip(from)], IsFinished, _changed.Task);
        }
    }

    private void Signal()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The states of a run.</summary>
internal static class RunStatus
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Aborted = "aborted";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
