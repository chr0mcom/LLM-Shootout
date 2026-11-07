using System.Text.Json;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Web.Runs;

/// <summary>
/// One match that runs (or ran last) in this process. It keeps every protocol line, so a browser that connects late,
/// or reconnects, gets the whole match again and then follows it live.
/// </summary>
internal sealed class ActiveRun(string id, string directory, StartRequest request, DateTimeOffset startedAt, CancellationTokenSource cancellation, TimeProvider time)
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private readonly Dictionary<Seat, DateTimeOffset> _busy = [];
    private int _busyVersion;
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
            _busy.Clear();
            _busyVersion++;
            Signal();
        }
    }

    /// <summary>Notes that a seat is waiting for its player to answer (or no longer is).</summary>
    public void SetBusy(Seat seat, bool busy)
    {
        lock (_gate)
        {
            if (busy)
            {
                _busy[seat] = time.GetUtcNow();
            }
            else
            {
                _busy.Remove(seat);
            }

            _busyVersion++;
            Signal();
        }
    }

    /// <summary>The lines from <paramref name="from"/> on, whether the run is over, who is being waited for, and a task that completes when something new happens.</summary>
    public RunSnapshot Read(int from)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var busy = _busy.ToDictionary(pair => pair.Key.ToString(), pair => (object)pair.Value);
            busy["now"] = now;
            return new RunSnapshot([.. _lines.Skip(from)], IsFinished, _changed.Task, _busyVersion, JsonSerializer.Serialize(busy));
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

/// <summary>What a reader of a run gets: new protocol lines, the end flag, a signal, and who is currently being waited for.</summary>
internal sealed record RunSnapshot(IReadOnlyList<string> Lines, bool Finished, Task Changed, int BusyVersion, string BusyJson);

/// <summary>The states of a run.</summary>
internal static class RunStatus
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Aborted = "aborted";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
