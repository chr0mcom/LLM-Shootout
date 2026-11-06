using System.Text.Json;
using System.Text.Json.Nodes;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Models;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;

namespace LlmShootout.Arena.Annotation;

/// <summary>
/// Lets a judge model rate a finished match. The result goes to its own file and is marked as a derived
/// assessment; <c>events.jsonl</c> is never touched.
/// </summary>
public sealed class Annotator(PlayerGateway gateway, TimeProvider time)
{
    /// <summary>Asks the judge all questions of the task and writes the annotation file. Returns its path.</summary>
    public async Task<string> RunAsync(IAnnotationTask task, MatchRecord match, IPlayer judge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(judge);

        var entries = new List<AnnotationEntry>();
        foreach (var request in task.Build(match))
        {
            entries.Add(await AskAsync(request, judge, cancellationToken));
        }

        var file = new AnnotationFile(
            task.Name,
            judge.Name,
            "Derived assessment by a judge model. It is not part of the match protocol and may be wrong.",
            time.GetUtcNow(),
            entries);
        var path = Path.Combine(match.Directory, $"annotation.{task.Name}.{Slug(judge.Name)}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, AnnotationJson.Default.AnnotationFile), cancellationToken);
        return path;
    }

    private async Task<AnnotationEntry> AskAsync(AnnotationRequest request, IPlayer judge, CancellationToken cancellationToken)
    {
        var call = new PlayerCall(CallPurpose.Judge, [new PromptMessage(PromptRole.User, request.Prompt)], null, 1, _ => request.SampleAnswer);
        try
        {
            var reply = await gateway.CallAsync(judge, call, cancellationToken);
            foreach (var text in new[] { reply.Content, reply.Reasoning })
            {
                if (ReasoningJsonRecovery.TryExtractJsonObject(text, out var json))
                {
                    return new AnnotationEntry(request.Key, request.Seat, JsonNode.Parse(json)!.AsObject(), reply.Content, null);
                }
            }

            return new AnnotationEntry(request.Key, request.Seat, null, reply.Content, "The judge returned no JSON object.");
        }
        catch (PlayerFailureException ex)
        {
            return new AnnotationEntry(request.Key, request.Seat, null, null, ex.Message);
        }
    }

    private static string Slug(string text) =>
        new([.. text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-')]);
}
