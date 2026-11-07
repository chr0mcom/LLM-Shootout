using System.Text;
using System.Text.Json;

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

    /// <summary>Opens (and creates) an event log in a match directory. The default is the log of the match itself.</summary>
    public ProtocolWriter(string directory, string fileName = ProtocolFiles.Events)
    {
        Directory.CreateDirectory(directory);
        var stream = new FileStream(Path.Combine(directory, fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    /// <summary>Called with every event after it has been written, for example by a live view. Optional.</summary>
    public Action<MatchEvent>? Observer { get; init; }

    /// <summary>Appends one event.</summary>
    public async Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
    {
        await _writer.WriteLineAsync(ProtocolJson.ToLine(matchEvent).AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
        Observer?.Invoke(matchEvent);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

/// <summary>Reads an event log back.</summary>
public static class ProtocolReader
{
    /// <summary>
    /// Reads all events of a log file of a match directory in order. Versions <see cref="ProtocolVersion.Oldest"/> to
    /// <see cref="ProtocolVersion.Current"/> are read; a newer version throws an <see cref="InvalidDataException"/>.
    /// </summary>
    public static IReadOnlyList<MatchEvent> ReadAll(string directory, string fileName = ProtocolFiles.Events)
    {
        var path = Path.Combine(directory, fileName);
        return [.. ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => ReadEvent(line, path))];
    }

    private static MatchEvent ReadEvent(string line, string path)
    {
        var version = SchemaVersionOf(line);
        return version > ProtocolVersion.Current
            ? throw new InvalidDataException($"'{path}' uses protocol version {version}, but this program reads versions up to {ProtocolVersion.Current}. Update the program to read it.")
            : ProtocolJson.FromLine(line);
    }

    private static int SchemaVersionOf(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out var value) && value.TryGetInt32(out var version)
            ? version
            : ProtocolVersion.Oldest;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
