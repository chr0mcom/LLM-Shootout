using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Annotation;

/// <summary>One question for the judge.</summary>
/// <param name="Key">Identifies the answer inside the annotation file.</param>
/// <param name="Seat">The seat the question is about, or null.</param>
/// <param name="Prompt">The complete prompt for the judge.</param>
/// <param name="SampleAnswer">A valid answer as JSON text, returned by judges that are not models (random, scripted).</param>
public sealed record AnnotationRequest(string Key, Seat? Seat, string Prompt, string SampleAnswer = "{}");

/// <summary>A game specific rating of a finished match by a judge model. It never changes the protocol.</summary>
public interface IAnnotationTask
{
    /// <summary>Short name used in file names, for example <c>promise</c>.</summary>
    string Name { get; }

    /// <summary>Builds the questions for a match.</summary>
    IReadOnlyList<AnnotationRequest> Build(MatchRecord match);

    /// <summary>Checks the judge's answer. Returns a message if it is unusable (wrong value, missing field), otherwise null.</summary>
    string? Validate(AnnotationRequest request, JsonObject answer) => null;
}

/// <summary>The judge's answer to one request.</summary>
/// <param name="Key">The key of the request.</param>
/// <param name="Seat">The seat the question was about.</param>
/// <param name="Result">The parsed JSON answer, or null if none could be read.</param>
/// <param name="Raw">The unchanged text of the answer.</param>
/// <param name="Error">What went wrong, or null.</param>
/// <param name="Prompt">The prompt as sent to the judge.</param>
/// <param name="Reasoning">The reasoning channel of the judge, if delivered.</param>
/// <param name="Call">Model, provider, generation id, tokens, cost and latency of the judge call, if it is a model.</param>
/// <param name="RawExchange">The raw HTTP exchange without credentials, if the judge is a model.</param>
public sealed record AnnotationEntry(
    string Key,
    Seat? Seat,
    JsonObject? Result,
    string? Raw,
    string? Error,
    string? Prompt = null,
    string? Reasoning = null,
    Players.ModelCallInfo? Call = null,
    string? RawExchange = null);

/// <summary>The file an annotation run writes: clearly marked as a derived assessment, not as data of the match.</summary>
public sealed record AnnotationFile(
    string Task,
    string Judge,
    string Note,
    DateTimeOffset At,
    IReadOnlyList<AnnotationEntry> Entries);
