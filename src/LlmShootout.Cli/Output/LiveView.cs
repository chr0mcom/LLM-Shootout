using System.Globalization;
using LlmShootout.Arena.Matches;
using LlmShootout.Arena.Protocol;
using Spectre.Console;

namespace LlmShootout.Cli.Output;

/// <summary>
/// Prints a match as it happens (or as it is replayed): messages, moves, retries, results and the running cost.
/// Private thoughts only appear with <c>--show-thoughts</c>.
/// </summary>
public sealed class LiveView(IAnsiConsole console, bool showThoughts, CostMeter? cost = null)
{
    /// <summary>Prints one event.</summary>
    public void Handle(MatchEvent matchEvent)
    {
        switch (matchEvent)
        {
            case MatchStartedEvent s:
                Line($"[bold]{Markup.Escape(s.GameId)}[/]  A: {Markup.Escape(s.Players[Arena.Games.Seat.A].Name)}  vs  B: {Markup.Escape(s.Players[Arena.Games.Seat.B].Name)}  [dim](seed {s.Seed}, messages {(s.Communication ? "on" : "off")}, {s.Language})[/]");
                break;
            case ModelCallEvent { Purpose: Arena.Players.CallPurpose.Turn } c when c.Class != CallClass.Valid:
                Line($"  [yellow]![/] {c.Seat} attempt {c.Attempt}: {c.Class}{Detail(c)}");
                break;
            case ModelCallEvent { Purpose: Arena.Players.CallPurpose.Debrief } c when c.Class != CallClass.Valid:
                Line($"  [yellow]![/] debriefing {c.Seat} attempt {c.Attempt}: {c.Class}");
                break;
            case MessageEvent m:
                Line($"[bold]{m.Seat}[/]: {Markup.Escape(m.Text ?? "(no message)")}{Flags(m)}");
                break;
            case TurnEvent t:
                Turn(t);
                break;
            case FactEvent { Audience: Arena.Games.FactAudience.Everyone } f:
                Line($"  [dim]·[/] {Markup.Escape(f.Text)}");
                break;
            case MatchFinishedEvent f:
                Line($"[green]Result:[/] {Markup.Escape(f.Result.Outcome)}  " + string.Join("  ", f.Result.Seats.Select(p => $"{p.Key}: {p.Value.Score.ToString("0.##", CultureInfo.InvariantCulture)} ({Markup.Escape(p.Value.Label)})")) + CostText());
                break;
            case MatchAbortedEvent a:
                Line($"[red]Aborted:[/] {Markup.Escape(a.Reason)}: {Markup.Escape(a.Detail)}");
                break;
            case DebriefEvent d:
                Line($"[dim]Debriefing {d.Seat}: {(d.Valid ? "ok" : "failed")} ({d.Attempts} attempt(s))[/]");
                break;
        }
    }

    private void Turn(TurnEvent t)
    {
        if (t.Move is not null)
        {
            var retries = t.Attempts > 1 ? $", {t.Attempts} attempts" : string.Empty;
            Line($"[bold]{t.Seat}[/] plays [cyan]{Markup.Escape(t.Move)}[/][dim]{retries}{CostText()}[/]");
        }
        else if (t.Status == TurnStatus.Exhausted)
        {
            Line($"[bold]{t.Seat}[/] [red]delivers no valid move[/] after {t.Attempts} attempts");
        }

        if (showThoughts && t.Thought is not null)
        {
            Line($"  [dim italic]{t.Seat} thinks: {Markup.Escape(t.Thought)}[/]");
        }
    }

    private static string Detail(ModelCallEvent c) =>
        c.Violations.Count > 0 ? ": " + Markup.Escape(string.Join("; ", c.Violations)) : c.Error is not null ? ": " + Markup.Escape(c.Error) : string.Empty;

    private static string Flags(MessageEvent m)
    {
        var flags = new List<string>();
        if (m.Truncated)
        {
            flags.Add("shortened");
        }

        if (m.IdentityHits.Count > 0)
        {
            flags.Add("identity: " + string.Join(", ", m.IdentityHits));
        }

        return flags.Count == 0 ? string.Empty : $" [dim]({Markup.Escape(string.Join("; ", flags))})[/]";
    }

    private string CostText() => cost is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"  ${cost.TotalUsd:0.0000}");

    private void Line(string markup) => console.MarkupLine(markup);
}
