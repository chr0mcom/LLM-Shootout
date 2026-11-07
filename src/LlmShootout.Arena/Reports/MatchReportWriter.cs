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
            match.Result?.Outcome,
            PlayMetrics.ResultWithRuleViolation(match),
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
        var play = PlayMetrics.For(match, seat);
        return new SeatSummary(
            player.Name,
            player.RequestedModel,
            infos.Select(i => i.DeliveredModel).OfType<string>().LastOrDefault(),
            result?.Score,
            result?.Label,
            match.Turns(seat).Count(),
            play.InvalidAnswers,
            play.Refusals,
            play.TokenLimitHits,
            play.RuleViolations,
            play.Resignations,
            play.ExhaustedTurns,
            match.Turns(seat).Count(t => t.ThoughtTruncated),
            match.Messages(seat).Count(m => m.Truncated),
            match.Messages(seat).Count(m => m.Silent),
            match.Messages(seat).Sum(m => m.IdentityHits.Count),
            infos.Sum(i => i.PromptTokens ?? 0),
            infos.Sum(i => i.CompletionTokens ?? 0),
            infos.Sum(i => i.ReasoningTokens ?? 0),
            infos.Sum(i => i.CostUsd ?? i.EstimatedCostUsd ?? 0m),
            match.Debriefs.LastOrDefault(d => d.Seat == seat)?.Valid,
            Summarize(play.Strength),
            Summarize(play.Calibration),
            play.ViolationInLostPositionThenWon);
    }

    private static StrengthSummary? Summarize(StrengthMetrics? strength) => strength is null
        ? null
        : new StrengthSummary(
            strength.MovesInWonPosition,
            strength.OptimalMoves,
            strength.SquanderedWins,
            strength.FirstMistake?.TurnId,
            strength.FirstMistake?.MoveNumber,
            strength.MistakeRate?.Value);

    private static CalibrationSummary? Summarize(CalibrationMetrics? calibration) => calibration is null
        ? null
        : new CalibrationSummary(
            Count(calibration.EstimateInWonPositions),
            calibration.EstimateInWonPositions?.Value,
            Count(calibration.EstimateInLostPositions),
            calibration.EstimateInLostPositions?.Value,
            Count(calibration.Brier),
            calibration.Brier?.Value);

    private static int Count(Fraction? fraction) => (int)(fraction?.Denominator ?? 0);

    /// <summary>Builds the Markdown report.</summary>
    public static string BuildReport(IGameDefinition definition, MatchRecord match)
    {
        var language = match.Started.Language;
        var t = new ReportText(ArenaTemplates.Default, language);
        var game = new ReportText(definition.Templates, language);
        var summary = Summarize(match);
        var sb = new StringBuilder();
        sb.AppendLine(t.Format("report.title", ("game", definition.DisplayName), ("a", match.Started.Players[Seat.A].Name), ("b", match.Started.Players[Seat.B].Name))).AppendLine();
        AppendAbort(sb, match, t);
        AppendOverview(sb, summary, t, game);
        AppendResult(sb, match, t, game);
        AppendHistory(sb, match, t);
        foreach (var section in definition.Analyzer?.AnalyzeMatch(match) ?? [])
        {
            sb.AppendLine().AppendLine("## " + section.Title).AppendLine().AppendLine(section.Markdown);
        }

        PlayStrengthSection.Append(sb, match, t);
        RuleComplianceSection.Append(sb, match, t);
        AppendThoughts(sb, match, t);
        AppendMetrics(sb, summary, t);
        AppendDebrief(sb, match, t);
        AppendFooter(sb, summary, t);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendAbort(StringBuilder sb, MatchRecord match, ReportText t)
    {
        if (match.Aborted is { } aborted)
        {
            sb.AppendLine(t.Format("report.aborted", ("reason", aborted.Reason), ("detail", aborted.Detail))).AppendLine();
        }
    }

    private static void AppendOverview(StringBuilder sb, MatchSummary s, ReportText t, ReportText game)
    {
        sb.AppendLine("## " + t["report.overview"]).AppendLine().AppendLine("| | |").AppendLine("|---|---|");
        Row(sb, t["report.label.date"], s.StartedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        Row(sb, t["report.label.seed"], s.Seed.ToString(CultureInfo.InvariantCulture));
        Row(sb, t["report.label.language"], s.Language);
        Row(sb, t["report.label.communication"], t[s.Communication ? "report.communication.on" : "report.communication.off"]);
        Row(sb, t["report.label.first_seat"], s.FirstSeat.ToString());
        Row(sb, t["report.label.outcome"], s.Outcome is { } outcome ? game.Word("result.outcome", outcome) : "-");
        Row(sb, t["report.label.duration"], TimeSpan.FromSeconds(s.DurationSeconds).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture));
        Row(sb, t["report.label.cost"], s.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        Row(sb, t["report.label.version"], $"{s.AppVersion} ({s.GitCommit})");
        sb.AppendLine();
    }

    private static void AppendResult(StringBuilder sb, MatchRecord match, ReportText t, ReportText game)
    {
        if (match.Result is not { } result)
        {
            return;
        }

        sb.AppendLine("## " + t["report.result"]).AppendLine().AppendLine(t["report.result.header"]);
        foreach (var seat in Seat.All)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {seat} | {MarkdownTable.Cell(match.Started.Players[seat].Name)} | {game.Word("result.label", result.Seats[seat].Label)} | {result.Seats[seat].Score.ToString("0.##", CultureInfo.InvariantCulture)} |");
        }

        sb.AppendLine();
    }

    private static void AppendHistory(StringBuilder sb, MatchRecord match, ReportText t)
    {
        sb.AppendLine("## " + t["report.history"]).AppendLine();
        foreach (var e in match.Events)
        {
            var line = e switch
            {
                MessageEvent m => MessageLine(m, t),
                TurnEvent { Move: not null } turn => t.Format("report.history.move", ("speaker", Speaker(turn.Seat, t)), ("move", turn.Move), ("attempts", turn.Attempts.ToString(CultureInfo.InvariantCulture))),
                TurnEvent { Status: TurnStatus.Exhausted } turn => t.Format("report.history.exhausted", ("speaker", Speaker(turn.Seat, t)), ("attempts", turn.Attempts.ToString(CultureInfo.InvariantCulture))),
                FactEvent fact => FactLine(fact, t),
                _ => null,
            };
            if (line is not null)
            {
                sb.AppendLine(line).AppendLine();
            }
        }
    }

    private static string MessageLine(MessageEvent m, ReportText t)
    {
        var flags = new List<string>();
        if (m.Truncated)
        {
            flags.Add(t["report.flag.truncated"]);
        }

        if (m.Silent)
        {
            flags.Add(t["report.flag.silent"]);
        }

        if (m.IdentityHits.Count > 0)
        {
            flags.Add(t.Format("report.flag.identity", ("hits", string.Join(", ", m.IdentityHits))));
        }

        var line = t.Format("report.history.message", ("speaker", Speaker(m.Seat, t)), ("text", m.Text ?? Silence(t)));
        return flags.Count == 0 ? line : $"{line} *({string.Join("; ", flags)})*";
    }

    private static string Silence(ReportText t) => t["history.silence"];

    private static string FactLine(FactEvent fact, ReportText t) => fact.Audience switch
    {
        FactAudience.Seat => t.Format("report.history.fact.private", ("text", fact.Text), ("seat", Speaker(fact.Seat ?? Seat.A, t))),
        FactAudience.Analysis => t.Format("report.history.fact.analysis", ("text", fact.Text)),
        _ => t.Format("report.history.fact", ("text", fact.Text)),
    };

    private static void AppendThoughts(StringBuilder sb, MatchRecord match, ReportText t)
    {
        var any = match.Turns().Any(x => x.Thought is not null);
        if (!any)
        {
            return;
        }

        sb.AppendLine().AppendLine("## " + t["report.thoughts"]).AppendLine().AppendLine(t["report.thoughts.note"]).AppendLine();
        foreach (var seat in Seat.All)
        {
            sb.AppendLine("### " + Speaker(seat, t)).AppendLine();
            foreach (var turn in match.Turns(seat).Where(x => x.Thought is not null))
            {
                var flag = turn.ThoughtTruncated ? $" *({t["report.flag.truncated"]})*" : string.Empty;
                sb.AppendLine(t.Format("report.thoughts.line", ("turn", turn.TurnId), ("thought", turn.Thought!), ("flag", flag)));
            }

            sb.AppendLine();
        }
    }

    private static void AppendMetrics(StringBuilder sb, MatchSummary s, ReportText t)
    {
        sb.AppendLine("## " + t["report.metrics"]).AppendLine().AppendLine(t["report.metrics.header"]);
        SeatRow(sb, t["report.metric.model"], s, x => $"{x.RequestedModel ?? x.Player} / {x.DeliveredModel ?? "-"}");
        SeatRow(sb, t["report.metric.turns"], s, x => x.Turns.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t["report.metric.thoughts_truncated"], s, x => x.ThoughtsTruncated.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t["report.metric.messages_truncated"], s, x => x.MessagesTruncated.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t["report.metric.silent"], s, x => x.SilentMessages.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t["report.metric.identity_hits"], s, x => x.IdentityHits.ToString(CultureInfo.InvariantCulture));
        SeatRow(sb, t["report.metric.tokens"], s, x => $"{x.PromptTokens} / {x.CompletionTokens} / {x.ReasoningTokens}");
        SeatRow(sb, t["report.metric.cost"], s, x => x.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static void AppendDebrief(StringBuilder sb, MatchRecord match, ReportText t)
    {
        sb.AppendLine("## " + t["report.debrief"]).AppendLine();
        var debriefs = Seat.All.ToDictionary(seat => seat, seat => match.Debriefs.LastOrDefault(d => d.Seat == seat));
        if (debriefs.Values.All(d => d is null))
        {
            sb.AppendLine(t["report.debrief.none"]).AppendLine();
            return;
        }

        sb.AppendLine(t["report.debrief.header"]);
        var keys = debriefs.Values.OfType<DebriefEvent>().SelectMany(d => d.Answers.Keys).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var key in keys)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| `{key}` | {MarkdownTable.Cell(DebriefAnswer(debriefs[Seat.A], key, t))} | {MarkdownTable.Cell(DebriefAnswer(debriefs[Seat.B], key, t))} |");
        }

        sb.AppendLine();
        foreach (var (seat, debrief) in debriefs.Where(d => d.Value is { Valid: false }))
        {
            sb.AppendLine(t.Format("report.debrief.failed", ("seat", seat.ToString()), ("problems", string.Join("; ", debrief!.Problems)))).AppendLine();
        }
    }

    private static string DebriefAnswer(DebriefEvent? debrief, string key, ReportText t) =>
        debrief?.Answers.GetValueOrDefault(key) is { } answer ? t.Answer(answer) : "-";

    private static void AppendFooter(StringBuilder sb, MatchSummary s, ReportText t)
    {
        if (s.CostEstimatedCalls > 0)
        {
            sb.AppendLine(t.Format("report.cost_estimated", ("n", s.CostEstimatedCalls.ToString(CultureInfo.InvariantCulture)))).AppendLine();
        }

        if (s.CostUnknownCalls > 0)
        {
            sb.AppendLine(t.Format("report.cost_unknown", ("n", s.CostUnknownCalls.ToString(CultureInfo.InvariantCulture)))).AppendLine();
        }
    }

    private static void SeatRow(StringBuilder sb, string label, MatchSummary s, Func<SeatSummary, string> value) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {MarkdownTable.Cell(value(s.Seats[Seat.A]))} | {MarkdownTable.Cell(value(s.Seats[Seat.B]))} |");

    private static void Row(StringBuilder sb, string label, string value) => sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {MarkdownTable.Cell(value)} |");

    private static string Speaker(Seat seat, ReportText t) => t.Format("who.seat", ("seat", seat.ToString()));

}
