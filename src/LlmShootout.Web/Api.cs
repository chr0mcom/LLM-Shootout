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
        api.MapGet("/matches/{name}/report", (string name, MatchArchive archive) => File(archive.OpenFile(name, ProtocolFiles.Report), "text/markdown; charset=utf-8"));
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

            await Task.Delay(TimeSpan.FromMilliseconds(600), context.RequestAborted);
            return Results.Redirect("/login?error=1", preserveMethod: false);
        }).RequireRateLimiting(PasswordGate.LoginRateLimit).DisableAntiforgery();
        app.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login", preserveMethod: false);
        });
    }

    private static async Task<IResult> ConfigAsync(PlayerSetup players, IOptions<WebOptions> web, RunManager runs, CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            games = GameCatalog.All.Select(g => new { id = g.Id, name = g.DisplayName }),
            endpoints = players.EndpointNames,
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
            return Results.Accepted($"/api/runs/{(await runs.StartAsync(request, cancellationToken)).Id}/stream", Describe(runs.Current));
        }
        catch (StartRejectedException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static IResult Cancel(string id, RunManager runs) =>
        runs.Cancel(id) ? Results.Accepted() : Results.Json(new { error = "No such running match." }, statusCode: StatusCodes.Status404NotFound);

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
        var sent = int.TryParse(context.Request.Headers["Last-Event-ID"], out var last) ? last + 1 : 0;
        var token = context.RequestAborted;
        while (!token.IsCancellationRequested)
        {
            var (lines, finished, changed) = run.Read(sent);
            var batch = new StringBuilder();
            foreach (var line in lines)
            {
                batch.Append("id: ").Append(sent++).Append("\nevent: protocol\ndata: ").Append(line).Append("\n\n");
            }

            if (finished)
            {
                batch.Append("event: end\ndata: ").Append(run.Status).Append("\n\n");
            }

            if (batch.Length > 0)
            {
                await context.Response.WriteAsync(batch.ToString(), token);
                await context.Response.Body.FlushAsync(token);
            }

            if (finished)
            {
                return;
            }

            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                await Task.WhenAny(changed, Task.Delay(Heartbeat, wait.Token));
                await wait.CancelAsync();
            }

            if (!changed.IsCompleted && !token.IsCancellationRequested)
            {
                await context.Response.WriteAsync(": keep-alive\n\n", token);
                await context.Response.Body.FlushAsync(token);
            }
        }
    }

    private static object? Describe(ActiveRun? run) => run is null
        ? null
        : new { id = run.Id, status = run.Status, match = run.MatchName, startedAt = run.StartedAt, error = run.Error, game = run.Request.Game };

    private static IResult File(FileStream? stream, string contentType) =>
        stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
}
