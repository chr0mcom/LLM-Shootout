using System.Text;
using LlmShootout.Arena.Protocol;
using LlmShootout.Games;
using LlmShootout.Web.Auth;
using LlmShootout.Web.Matches;
using LlmShootout.Web.Runs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web;

/// <summary>All routes of the interface: login and logout, the JSON API and the live stream.</summary>
internal static class Api
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailedLoginDelay = TimeSpan.FromMilliseconds(600);

    public static void MapShootout(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Text("ok"));
        MapLogin(app);
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/config", ConfigAsync);
        api.MapGet("/runs/current", CurrentRun);
        api.MapPost("/runs", StartAsync);
        api.MapGet("/runs/{id}/stream", StreamAsync);
        api.MapPost("/runs/{id}/cancel", Cancel);
        api.MapGet("/matches", (MatchArchive archive) => archive.List());
        api.MapGet("/matches/{name}/events", (string name, MatchArchive archive) => File(archive.OpenFile(name, ProtocolFiles.Events), "text/plain; charset=utf-8"));
        api.MapGet("/matches/{name}/report", (string name, MatchArchive archive) => File(archive.OpenFile(name, ProtocolFiles.Report), "text/plain; charset=utf-8"));
        api.MapGet("/matches/{name}/export.xlsx", Export);
    }

    private static void MapLogin(IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "login.html"), "text/html; charset=utf-8"));
        app.MapPost("/login", async (HttpContext context, IOptions<WebOptions> web) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (PasswordGate.PasswordMatches(form["password"], web.Value.Password))
            {
                await PasswordGate.SignInAsync(context, web.Value.Password);
                return Results.Redirect("/", preserveMethod: false);
            }

            await Task.Delay(FailedLoginDelay, context.RequestAborted);
            return Results.Redirect("/login?error=1", preserveMethod: false);
        }).RequireRateLimiting(PasswordGate.LoginRateLimit).DisableAntiforgery();
        app.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login", preserveMethod: false);
        });
    }

    private static async Task<IResult> ConfigAsync(IPlayerSource players, IOptions<WebOptions> web, RunManager runs, CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            games = GameCatalog.All.Select(g => new { id = g.Id, name = g.DisplayName, defaultCommunication = g.DefaultCommunication }),
            players = await players.SuggestionsAsync(cancellationToken),
            maxCostCapUsd = web.Value.MaxCostCapUsd,
            defaultMaxCostUsd = web.Value.DefaultMaxCostUsd,
            current = Describe(runs.Current),
        });

    private static IResult CurrentRun(RunManager runs) => Results.Ok(new { run = Describe(runs.Current) });

    private static async Task<IResult> StartAsync(StartRequest request, RunManager runs, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Accepted($"/api/runs/{(await runs.StartMatchAsync(request, cancellationToken)).Id}/stream", Describe(runs.Current));
        }
        catch (StartRejectedException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static IResult Cancel(string id, RunManager runs) =>
        runs.Cancel(id) ? Results.Accepted() : Results.Json(new { error = "No such running match." }, statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// The live stream of one run as Server-Sent Events: first every protocol line so far, then new ones as they
    /// come, a <c>progress</c> event when the player being waited for changes, and finally <c>end</c>. A reconnecting
    /// browser sends <c>Last-Event-ID</c> and continues after the last line it has seen.
    /// </summary>
    private static async Task StreamAsync(string id, HttpContext context, RunManager runs)
    {
        if (runs.Find(id) is not { } run)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        var nextLine = int.TryParse(context.Request.Headers["Last-Event-ID"], out var last) ? last + 1 : 0;
        var progressSent = -1;
        var token = context.RequestAborted;
        while (!token.IsCancellationRequested)
        {
            var snapshot = run.Read(nextLine);
            var events = FormatEvents(snapshot, run.Status, ref nextLine, ref progressSent);
            if (events.Length > 0)
            {
                await WriteAsync(context.Response, events, token);
            }

            if (snapshot.Finished)
            {
                return;
            }

            if (!await WaitForChangeAsync(snapshot.Changed, token))
            {
                await WriteAsync(context.Response, ": keep-alive\n\n", token);
            }
        }
    }

    private static string FormatEvents(RunSnapshot snapshot, string status, ref int nextLine, ref int progressSent)
    {
        var events = new StringBuilder();
        foreach (var line in snapshot.Lines)
        {
            events.Append("id: ").Append(nextLine++).Append("\nevent: protocol\ndata: ").Append(line).Append("\n\n");
        }

        if (snapshot.BusyVersion != progressSent)
        {
            progressSent = snapshot.BusyVersion;
            events.Append("event: progress\ndata: ").Append(snapshot.BusyJson).Append("\n\n");
        }

        if (snapshot.Finished)
        {
            events.Append("event: end\ndata: ").Append(status).Append("\n\n");
        }

        return events.ToString();
    }

    /// <summary>Waits for news; returns false if the heartbeat interval passed without any.</summary>
    private static async Task<bool> WaitForChangeAsync(Task changed, CancellationToken token)
    {
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Task.WhenAny(changed, Task.Delay(Heartbeat, heartbeat.Token));
        await heartbeat.CancelAsync();
        return changed.IsCompleted || token.IsCancellationRequested;
    }

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken token)
    {
        await response.WriteAsync(text, token);
        await response.Body.FlushAsync(token);
    }

    private static IResult Export(string name, MatchArchive archive)
    {
        var directory = archive.Resolve(name);
        if (directory is null)
        {
            return Results.NotFound();
        }

        try
        {
            return Results.File(MatchExport.ToWorkbook(MatchRecord.Load(directory)), XlsxWriter.ContentType, $"{name}.xlsx");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            return Results.Json(new { error = "This match is not complete enough to export yet." }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static object? Describe(ActiveRun? run) => run is null
        ? null
        : new { id = run.Id, status = run.Status, match = run.MatchName, startedAt = run.StartedAt, error = run.Error, game = run.Request.Game };

    private static IResult File(FileStream? stream, string contentType) =>
        stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
}
