using System.Globalization;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Players;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Reports;

namespace LlmShootout.Web.Matches;

/// <summary>Turns a stored match into the sheets of an Excel workbook: overview, transcript, debriefing, calls and raw answers.</summary>
internal static class MatchExport
{
    public static byte[] ToWorkbook(MatchRecord match) => XlsxWriter.Write(
    [
        Overview(match),
        Transcript(match),
        Debriefing(match),
        Calls(match),
        RawAnswers(match),
    ]);

    internal static Sheet Overview(MatchRecord match)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            Row("Game", match.GameId),
            Row("Started (UTC)", match.Started.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Row("Duration (seconds)", Math.Round(match.Duration.TotalSeconds, 1)),
            Row("Prompt language", match.Started.Language),
            Row("Players could talk", match.Communication ? "yes" : "no"),
            Row("First seat", match.Started.FirstSeat.ToString()),
            Row("Seed", match.Started.Seed),
            Row("Outcome", match.Result?.Outcome ?? "not finished"),
            Row("Result with rule violation", PlayMetrics.ResultWithRuleViolation(match) ? "yes" : "no"),
        };
        if (match.Aborted is { } aborted)
        {
            rows.Add(Row("Stopped", $"{aborted.Reason}: {aborted.Detail}"));
        }

        foreach (var seat in Seat.All)
        {
            var result = match.Result?.Seats.GetValueOrDefault(seat);
            var delivered = match.Calls.Where(c => c.Seat == seat && c.Call?.DeliveredModel is not null).Select(c => c.Call!.DeliveredModel).LastOrDefault();
            rows.Add(Row($"Seat {seat}: player", match.Started.Players[seat].Name));
            rows.Add(Row($"Seat {seat}: answered by", delivered));
            rows.Add(Row($"Seat {seat}: score", result?.Score));
            rows.Add(Row($"Seat {seat}: result", result?.Label));
            rows.Add(Row($"Seat {seat}: invalid answers", match.InvalidAnswers(seat)));
            rows.Add(Row($"Seat {seat}: refusals", match.Refusals(seat)));
            rows.Add(Row($"Seat {seat}: rule violations", PlayMetrics.For(match, seat).RuleViolations));
        }

        rows.Add(Row("Model calls", match.Calls.Count()));
        rows.Add(Row("Cost (USD)", new Money(match.CostUsd)));
        rows.Add(Row("Application version", $"{match.Started.AppVersion} {match.Started.GitCommit}".Trim()));
        return new Sheet("Match", ["Item", "Value"], rows, [30, 60]);
    }

    internal static Sheet Transcript(MatchRecord match)
    {
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var e in match.Events)
        {
            switch (e)
            {
                case ModelCallEvent { Purpose: CallPurpose.Turn } call when call.Class == CallClass.Valid && !string.IsNullOrWhiteSpace(call.Parsed?.Thought):
                    rows.Add(Row(call.TurnId, call.Seat.ToString(), "Private thought", call.Parsed!.Thought));
                    break;
                case ModelCallEvent { Purpose: CallPurpose.Turn } call when call.Class != CallClass.Valid:
                    rows.Add(Row(call.TurnId, call.Seat.ToString(), $"Problem (try {call.Attempt})", $"{call.Class}: {call.Error ?? string.Join("; ", call.Violations)}"));
                    break;
                case MessageEvent message:
                    rows.Add(Row(message.TurnId, message.Seat.ToString(), "Message", message.Silent ? "(silence)" : message.Text));
                    break;
                case TurnEvent { Status: TurnStatus.Exhausted } turn:
                    rows.Add(Row(turn.TurnId, turn.Seat.ToString(), "Move", "(no valid answer)"));
                    break;
                case TurnEvent { Move: not null } turn:
                    rows.Add(Row(turn.TurnId, turn.Seat.ToString(), "Move", turn.Move));
                    break;
                case FactEvent fact:
                    rows.Add(Row(fact.TurnId, fact.Seat?.ToString(), fact.Audience switch { FactAudience.Everyone => "Fact", FactAudience.Seat => "Private fact", _ => "Analysis" }, fact.Text));
                    break;
                case MatchAbortedEvent aborted:
                    rows.Add(Row(null, null, "Stopped", $"{aborted.Reason}: {aborted.Detail}"));
                    break;
            }
        }

        return new Sheet("Transcript", ["Step", "Seat", "What", "Text"], rows, [14, 8, 18, 100]);
    }

    internal static Sheet Debriefing(MatchRecord match)
    {
        var debriefs = match.Debriefs.GroupBy(d => d.Seat).ToDictionary(g => g.Key, g => g.Last());
        var keys = debriefs.Values.SelectMany(d => d.Answers.Keys).Distinct().ToList();
        var rows = keys
            .Select(key => Row(key.Replace('_', ' '), Answer(debriefs, Seat.A, key), Answer(debriefs, Seat.B, key)))
            .ToList();
        return new Sheet("Debriefing", ["Question", "Seat A", "Seat B"], rows, [34, 70, 70]);
    }

    internal static Sheet Calls(MatchRecord match)
    {
        var rows = match.Calls.Select(c => Row(
            c.TurnId,
            c.Seat.ToString(),
            c.Attempt,
            c.Purpose.ToString(),
            c.Class,
            c.Call?.PromptTokens,
            c.Call?.CompletionTokens,
            c.Call?.ReasoningTokens,
            c.Call is null ? null : Math.Round(c.Call.Latency.TotalSeconds, 1),
            new Money(c.Call?.CostUsd ?? c.Call?.EstimatedCostUsd ?? 0m),
            c.Call?.DeliveredModel ?? c.Call?.RequestedModel,
            c.Call?.Provider,
            c.Error ?? string.Join("; ", c.Violations))).ToList();
        return new Sheet("Calls", ["Step", "Seat", "Try", "Purpose", "Result", "Tokens in", "Tokens out", "Thinking tokens", "Seconds", "USD", "Model", "Provider", "Problems"], rows, [14, 8, 6, 10, 14, 11, 11, 15, 10, 10, 28, 16, 50]);
    }

    internal static Sheet RawAnswers(MatchRecord match)
    {
        var rows = match.Calls.Select(c => Row(c.TurnId, c.Seat.ToString(), c.Attempt, c.RawContent, c.RawReasoning)).ToList();
        return new Sheet("Raw answers", ["Step", "Seat", "Try", "Answer as received", "Reasoning"], rows, [14, 8, 6, 90, 90]);
    }

    private static string? Answer(Dictionary<Seat, DebriefEvent> debriefs, Seat seat, string key) =>
        debriefs.TryGetValue(seat, out var d) && d.Answers.TryGetValue(key, out var value) ? value : null;

    private static List<object?> Row(params object?[] cells) => [.. cells];
}
