using System.Globalization;
using System.Text;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Protocol;
using LlmShootout.Arena.Templates;

namespace LlmShootout.Arena.Reports;

/// <summary>Turns a protocol into <c>match.json</c> (numbers) and <c>report.md</c> (a readable account). Works from the protocol alone.</summary>
public static class MatchReportWriter
{
    /// <summary>Writes both files into the match directory.</summary>
    public static async Task WriteAsync(IGameDefinition definition, MatchRecord match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(match);
        await File.WriteAllTextAsync(Path.Combine(match.Directory, ProtocolFiles.Summary), SummaryJson.Write(Summarize(match)), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(match.Directory, ProtocolFiles.Report), BuildReport(definition, match), cancellationToken);
    }

    /// <summary>Collects the numbers of a match.</summary>
    public static MatchSummary Summarize(MatchRecord match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var started = match.Started;
        var seats = Seat.All.ToDictionary(seat => seat, seat => SummarizeSeat(match, seat));
        return new MatchSummary(
            match.GameId,
            started.At,
            match.Duration.TotalSeconds,
            match.Aborted is not null,
            match.Aborted?.Reason,
            match.Communication,
            started.Language,
            started.Seed,
            started.FirstSeat,
            started.Options.RevealIdentity,
            match.Result?.Outcome,
            seats,
            match.CostUsd,
            match.CostEstimatedCalls,
            match.CostUnknownCalls,
            match.Calls.Count(),
            started.AppVersion,
            started.GitCommit,
            started.TemplateHashes);
    }

    private static SeatSummary SummarizeSeat(MatchRecord match, Seat seat)
    {
        var calls = match.Calls.Where(c => c.Seat == seat).ToArray();
        var infos = calls.Select(c => c.Call).OfType<Players.ModelCallInfo>().ToArray();
        var player = match.Started.Players[seat];
        var result = match.Result?.Seats[seat];
        return new SeatSummary(
            player.Name,
            player.RequestedModel,
            infos.Select(i => i.DeliveredModel).OfType<string>().LastOrDefault(),
            result?.Score,
            result?.Label,
            match.Turns(seat).Count(),
            match.InvalidAnswers(seat),
            match.Refusals(seat),
            match.TokenLimitHits(seat),
            match.Turns(seat).Count(t => t.ThoughtTruncated),
            match.Messages(seat).Count(m => m.Truncated),
            match.Messages(seat).Count(m => m.Silent),
            match.Messages(seat).Sum(m => m.IdentityHits.Count),
            infos.Sum(i => i.PromptTokens ?? 0),
            infos.Sum(i => i.CompletionTokens ?? 0),
            infos.Sum(i => i.ReasoningTokens ?? 0),
            infos.Sum(i => i.CostUsd ?? i.EstimatedCostUsd ?? 0m),
            match.Debriefs.LastOrDefault(d => d.Seat == seat)?.Valid);
    }

    /// <summary>Builds the Markdown report.</summary>
    public static string BuildReport(IGameDefinition definition, MatchRecord match)
    {
        var language = match.Started.Language;
        string T(string key, params (string, string)[] values) => ArenaTemplates.Default.Render(TemplateRef.Of(key, values), language);
        var summary = Summarize(match);
        var sb = new StringBuilder();
        sb.AppendLine(T("report.title", ("game", definition.DisplayName), ("a", match.Started.Players[Seat.A].Name), ("b", match.Started.Players[Seat.B].Name))).AppendLine();
        AppendAbort(sb, match, T);
        AppendOverview(sb, summary, T);
        AppendResult(sb, match, T);
        AppendHistory(sb, match, T);
        foreach (var section in definition.Analyzer?.AnalyzeMatch(match) ?? [])
        {
            sb.AppendLine().AppendLine("## " + section.Title).AppendLine().AppendLine(section.Markdown);
        }

        AppendIdentity(sb, match, T);
        AppendThoughts(sb, match, T);
        AppendMetrics(sb, summary, T);
        AppendDebrief(sb, match, T);
        AppendFooter(sb, summary, T);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendAbort(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        if (match.Aborted is { } aborted)
        {
            sb.AppendLine(t("report.aborted", [("reason", aborted.Reason), ("detail", aborted.Detail)])).AppendLine();
        }
    }

    private static void AppendOverview(StringBuilder sb, MatchSummary s, Func<string, (string, string)[], string> t)
    {
        sb.AppendLine("## " + t("report.overview", [])).AppendLine().AppendLine("| | |").AppendLine("|---|---|");
        Row(sb, t("report.label.date", []), s.StartedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        Row(sb, t("report.label.seed", []), s.Seed.ToString(CultureInfo.InvariantCulture));
        Row(sb, t("report.label.language", []), s.Language);
        Row(sb, t("report.label.communication", []), t(s.Communication ? "report.communication.on" : "report.communication.off", []));
        Row(sb, t("report.label.identity", []), t(s.IdentityRevealed ? "report.identity.revealed" : "report.identity.hidden", []));
        Row(sb, t("report.label.first_seat", []), s.FirstSeat.ToString());
        Row(sb, t("report.label.outcome", []), s.Outcome ?? "-");
        Row(sb, t("report.label.duration", []), TimeSpan.FromSeconds(s.DurationSeconds).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture));
        Row(sb, t("report.label.cost", []), s.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        Row(sb, t("report.label.version", []), $"{s.AppVersion} ({s.GitCommit})");
        sb.AppendLine();
    }

    private static void AppendResult(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        if (match.Result is not { } result)
        {
            return;
        }

        sb.AppendLine("## " + t("report.result", [])).AppendLine().AppendLine(t("report.result.header", []));
        foreach (var seat in Seat.All)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {seat} | {Cell(match.Started.Players[seat].Name)} | {result.Seats[seat].Label} | {result.Seats[seat].Score.ToString("0.##", CultureInfo.InvariantCulture)} |");
        }

        sb.AppendLine();
    }

    private static void AppendHistory(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        sb.AppendLine("## " + t("report.history", [])).AppendLine();
        foreach (var e in match.Events)
        {
            var line = e switch
            {
                MessageEvent m => MessageLine(m, t),
                TurnEvent { Move: not null } turn => t("report.history.move", [("speaker", Speaker(turn.Seat, t)), ("move", turn.Move), ("attempts", turn.Attempts.ToString(CultureInfo.InvariantCulture))]),
                TurnEvent { Status: "exhausted" } turn => t("report.history.exhausted", [("speaker", Speaker(turn.Seat, t)), ("attempts", turn.Attempts.ToString(CultureInfo.InvariantCulture))]),
                FactEvent fact => FactLine(fact, t),
                _ => null,
            };
            if (line is not null)
            {
                sb.AppendLine(line).AppendLine();
            }
        }
    }

    private static string MessageLine(MessageEvent m, Func<string, (string, string)[], string> t)
    {
        var flags = new List<string>();
        if (m.Truncated)
        {
            flags.Add(t("report.flag.truncated", []));
        }

        if (m.Silent)
        {
            flags.Add(t("report.flag.silent", []));
        }

        if (m.IdentityHits.Count > 0)
        {
            flags.Add(t("report.flag.identity", [("hits", string.Join(", ", m.IdentityHits))]));
        }

        var line = t("report.history.message", [("speaker", Speaker(m.Seat, t)), ("text", m.Text ?? Silence(t))]);
        return flags.Count == 0 ? line : $"{line} *({string.Join("; ", flags)})*";
    }

    private static string Silence(Func<string, (string, string)[], string> t) => t("history.silence", []);

    private static string FactLine(FactEvent fact, Func<string, (string, string)[], string> t) => fact.Audience switch
    {
        FactAudience.Seat => t("report.history.fact.private", [("text", fact.Text), ("seat", Speaker(fact.Seat ?? Seat.A, t))]),
        FactAudience.Analysis => t("report.history.fact.analysis", [("text", fact.Text)]),
        _ => t("report.history.fact", [("text", fact.Text)]),
    };

    private static void AppendIdentity(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        var guesses = Seat.All.ToDictionary(seat => seat, seat => match.Debriefs.LastOrDefault(d => d.Seat == seat && d.Valid)?.Answers.GetValueOrDefault("opponent_guess"));
        if (match.Started.Options.RevealIdentity || guesses.Values.All(g => g is null))
        {
            return;
        }

        int Hits(Seat author, string subject) =>
            match.Messages(author).Sum(m => m.IdentityHits.Count(h => h.StartsWith(subject + ":", StringComparison.Ordinal)));

        sb.AppendLine().AppendLine("## " + t("report.identity", [])).AppendLine().AppendLine(t("report.identity.header", []));
        foreach (var seat in Seat.All.Where(s => guesses[s] is not null))
        {
            var read = Hits(seat.Opponent, "self") + Hits(seat.Opponent, "ambiguous");
            var said = Hits(seat, "opponent") + Hits(seat, "ambiguous");
            var finding = t(read > 0 ? "report.identity.read" : "report.identity.guessed", []);
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {seat} | {Cell(guesses[seat]!)} | {read} | {said} | {finding} |");
        }

        sb.AppendLine().AppendLine(t("report.identity.note", [])).AppendLine();
    }

    private static void AppendThoughts(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        var any = match.Turns().Any(x => x.Thought is not null);
        if (!any)
        {
            return;
        }

        sb.AppendLine().AppendLine("## " + t("report.thoughts", [])).AppendLine().AppendLine(t("report.thoughts.note", [])).AppendLine();
        foreach (var seat in Seat.All)
        {
            sb.AppendLine("### " + Speaker(seat, t)).AppendLine();
            foreach (var turn in match.Turns(seat).Where(x => x.Thought is not null))
            {
                var flag = turn.ThoughtTruncated ? $" *({t("report.flag.truncated", [])})*" : string.Empty;
                sb.AppendLine(t("report.thoughts.line", [("turn", turn.TurnId), ("thought", turn.Thought!), ("flag", flag)]));
            }

            sb.AppendLine();
        }
    }

    private static void AppendMetrics(StringBuilder sb, MatchSummary s, Func<string, (string, string)[], string> t)
    {
        sb.AppendLine("## " + t("report.metrics", [])).AppendLine().AppendLine(t("report.metrics.header", []));
        SeatRow(sb, t("report.metric.model", []), s, x => $"{x.RequestedModel ?? x.Player} / {x.DeliveredModel ?? "-"}");
        SeatRow(sb, t("report.metric.turns", []), s, x => x.Turns.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.invalid", []), s, x => x.InvalidAnswers.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.refusals", []), s, x => x.Refusals.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.token_limit", []), s, x => x.TokenLimitHits.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.thoughts_truncated", []), s, x => x.ThoughtsTruncated.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.messages_truncated", []), s, x => x.MessagesTruncated.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.silent", []), s, x => x.SilentMessages.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.identity_hits", []), s, x => x.IdentityHits.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t("report.metric.tokens", []), s, x => $"{x.PromptTokens} / {x.CompletionTokens} / {x.ReasoningTokens}");
        SeatRow(sb, t("report.metric.cost", []), s, x => x.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static void AppendDebrief(StringBuilder sb, MatchRecord match, Func<string, (string, string)[], string> t)
    {
        sb.AppendLine("## " + t("report.debrief", [])).AppendLine();
        var debriefs = Seat.All.ToDictionary(seat => seat, seat => match.Debriefs.LastOrDefault(d => d.Seat == seat));
        if (debriefs.Values.All(d => d is null))
        {
            sb.AppendLine(t("report.debrief.none", [])).AppendLine();
            return;
        }

        sb.AppendLine(t("report.debrief.header", []));
        var keys = debriefs.Values.OfType<DebriefEvent>().SelectMany(d => d.Answers.Keys).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var key in keys)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| `{key}` | {Cell(debriefs[Seat.A]?.Answers.GetValueOrDefault(key) ?? "-")} | {Cell(debriefs[Seat.B]?.Answers.GetValueOrDefault(key) ?? "-")} |");
        }

        sb.AppendLine();
        foreach (var (seat, debrief) in debriefs.Where(d => d.Value is { Valid: false }))
        {
            sb.AppendLine(t("report.debrief.failed", [("seat", seat.ToString()), ("problems", string.Join("; ", debrief!.Problems))])).AppendLine();
        }
    }

    private static void AppendFooter(StringBuilder sb, MatchSummary s, Func<string, (string, string)[], string> t)
    {
        if (s.CostEstimatedCalls > 0)
        {
            sb.AppendLine(t("report.cost_estimated", [("n", s.CostEstimatedCalls.ToString(CultureInfo.InvariantCulture))])).AppendLine();
        }

        if (s.CostUnknownCalls > 0)
        {
            sb.AppendLine(t("report.cost_unknown", [("n", s.CostUnknownCalls.ToString(CultureInfo.InvariantCulture))])).AppendLine();
        }

        sb.AppendLine(t("report.derived", []));
    }

    private static void SeatRow(StringBuilder sb, string label, MatchSummary s, Func<SeatSummary, string> value) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {Cell(value(s.Seats[Seat.A]))} | {Cell(value(s.Seats[Seat.B]))} |");

    private static void Row(StringBuilder sb, string label, string value) => sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {Cell(value)} |");

    private static string Speaker(Seat seat, Func<string, (string, string)[], string> t) => t("who.seat", [("seat", seat.ToString())]);

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
