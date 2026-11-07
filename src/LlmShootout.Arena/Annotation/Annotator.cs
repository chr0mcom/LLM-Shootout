using LlmShootout.Arena.Answers;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Annotation;

/// <summary>
/// Lets a judge model rate a finished match. The result goes to its own file and is marked as a derived
/// assessment; <c>events.jsonl</c> is never touched.
/// </summary>
public sealed class Annotator(PlayerGateway gateway, TimeProvider time)
{
    /// <summary>Asks the judge all questions of the task and writes the annotation file. Returns its path, or null if the match gave nothing to ask.</summary>
    public async Task<string?> RunAsync(IAnnotationTask task, MatchRecord match, IPlayer judge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(judge);

        var requests = task.Build(match);
        if (requests.Count == 0)
        {
            return null;
        }

        var entries = new List<AnnotationEntry>();
        foreach (var request in requests)
        {
            entries.Add(await AskAsync(task, request, judge, cancellationToken));
        }

        var file = new AnnotationFile(
            task.Name,
            judge.Name,
            "Derived assessment by a judge model. It is not part of the match protocol and may be wrong.",
            time.GetUtcNow(),
            entries);
        var path = Path.Combine(match.Directory, $"annotation.{task.Name}.{Slug(judge.Name)}.json");
        await File.WriteAllTextAsync(path, AnnotationJson.Write(file), cancellationToken);
        return path;
    }

    private async Task<AnnotationEntry> AskAsync(IAnnotationTask task, AnnotationRequest request, IPlayer judge, CancellationToken cancellationToken)
    {
        var call = new PlayerCall(CallPurpose.Judge, [new PromptMessage(PromptRole.User, request.Prompt)], null, 1, _ => request.SampleAnswer);
        try
        {
            var reply = await gateway.CallAsync(judge, call, cancellationToken);
            if (JsonObjectExtractor.FindAnswer(reply.Content, reply.Reasoning) is var (answer, _))
            {
                var problem = task.Validate(request, answer);
                return new AnnotationEntry(request.Key, request.Seat, problem is null ? answer : null, reply.Content, problem, request.Prompt, reply.Reasoning, reply.Call, reply.RawExchange);
            }

            return new AnnotationEntry(request.Key, request.Seat, null, reply.Content, "The judge returned no JSON object.", request.Prompt, reply.Reasoning, reply.Call, reply.RawExchange);
        }
        catch (PlayerFailureException ex)
        {
            return new AnnotationEntry(request.Key, request.Seat, null, null, ex.Message, request.Prompt, null, ex.Call, ex.RawExchange);
        }
    }

    private static string Slug(string text) =>
        new([.. text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-')]);
}
