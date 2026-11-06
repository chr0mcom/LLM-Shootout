using System.Text;

namespace LlmShootout.Arena.Protocol;

/// <summary>Names of the files of a match directory.</summary>
public static class ProtocolFiles
{
    /// <summary>The append-only event log.</summary>
    public const string Events = "events.jsonl";

    /// <summary>The summary.</summary>
    public const string Summary = "match.json";

    /// <summary>The human readable report.</summary>
    public const string Report = "report.md";
}

/// <summary>Appends events to <c>events.jsonl</c>, one line each, flushed immediately so an aborted match stays readable.</summary>
public sealed class ProtocolWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>Opens (and creates) the event log in a match directory.</summary>
    public ProtocolWriter(string directory)
    {
        Directory.CreateDirectory(directory);
        var stream = new FileStream(Path.Combine(directory, ProtocolFiles.Events), FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    /// <summary>Appends one event.</summary>
    public async Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
    {
        await _writer.WriteLineAsync(ProtocolJson.ToLine(matchEvent).AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

/// <summary>Reads an event log back.</summary>
public static class ProtocolReader
{
    /// <summary>Reads all events of a match directory in order.</summary>
    public static IReadOnlyList<MatchEvent> ReadAll(string directory) =>
        [.. File.ReadLines(Path.Combine(directory, ProtocolFiles.Events))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ProtocolJson.FromLine)];
}
