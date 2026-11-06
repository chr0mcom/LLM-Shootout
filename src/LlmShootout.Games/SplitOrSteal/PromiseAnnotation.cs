using LlmShootout.Arena.Annotation;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Games.SplitOrSteal;

/// <summary>
/// Asks a judge model whether a player firmly announced how they would decide. The report works without it and then
/// shows messages and decision side by side.
/// </summary>
public sealed class PromiseAnnotation(ITemplateSource templates) : IAnnotationTask
{
    /// <summary>Name used in file names.</summary>
    public const string TaskName = "promise";

    /// <inheritdoc />
    public string Name => TaskName;

    /// <inheritdoc />
    public IReadOnlyList<AnnotationRequest> Build(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var requests = new List<AnnotationRequest>();
        foreach (var seat in Seat.All)
        {
            var messages = match.Messages(seat).Where(m => m.Text is not null).Select(m => $"- {m.Text}").ToArray();
            if (messages.Length > 0)
            {
                var prompt = templates.Render(TemplateRef.Of("judge.promise", ("messages", string.Join('\n', messages))), match.Started.Language);
                requests.Add(new AnnotationRequest(Key(seat), seat, prompt, "{\"announced\":\"unclear\",\"quote\":null,\"confidence\":0}"));
            }
        }

        return requests;
    }

    /// <summary>The key of the request for a seat.</summary>
    public static string Key(Seat seat) => $"promise-{seat}";

    /// <summary>What the judge found for a seat (<c>split</c>, <c>steal</c>, <c>none</c>, <c>unclear</c>), the judge's name, or null if no assessment exists.</summary>
    public static (string Announced, string Judge)? Find(MatchRecord match, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(match);
        foreach (var file in match.Annotations.Where(a => a.Task == TaskName))
        {
            var entry = file.Entries.FirstOrDefault(e => e.Key == Key(seat));
            if (entry?.Result?["announced"]?.ToString() is { } announced)
            {
                return (announced.Trim().ToLowerInvariant(), file.Judge);
            }
        }

        return null;
    }
}
