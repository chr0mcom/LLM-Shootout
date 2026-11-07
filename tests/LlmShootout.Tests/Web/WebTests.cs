using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LlmShootout.Web;
using LlmShootout.Web.Matches;
using Microsoft.Extensions.Options;

namespace LlmShootout.Tests.Web;

public sealed class WebTests : IDisposable
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly object RandomVersusRandom = new { game = "split-or-steal", a = "random", b = "random", language = "en", seed = 7, debrief = true };

    private readonly ShootoutFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<JsonNode> JsonOf(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    [InlineData("/index.html")]
    public async Task Pages_redirect_to_the_login_without_a_session(string path)
    {
        using var response = await _factory.NewClient().GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/api/config")]
    [InlineData("/api/matches")]
    [InlineData("/api/runs/current")]
    [InlineData("/api/matches/anything/events")]
    public async Task The_api_answers_401_without_a_session(string path)
    {
        using var response = await _factory.NewClient().GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/login.js")]
    [InlineData("/site.css")]
    [InlineData("/health")]
    public async Task The_login_page_and_its_assets_are_public(string path)
    {
        using var response = await _factory.NewClient().GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_gives_no_session()
    {
        var client = _factory.NewClient();

        using var login = await client.PostAsync("/login", new FormUrlEncodedContent([new("password", "wrong")]), Token);
        using var api = await client.GetAsync("/api/config", Token);

        Assert.Equal("/login?error=1", login.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task The_right_password_opens_the_arena_and_logout_closes_it()
    {
        var client = _factory.NewClient(signedIn: true);

        using var page = await client.GetAsync("/", Token);
        using var config = await client.GetAsync("/api/config", Token);
        using var logout = await client.PostAsync("/logout", null, Token);
        using var after = await client.GetAsync("/api/config", Token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("split-or-steal", (await JsonOf(config))["games"]![0]!["id"]!.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_secure_and_same_site_strict()
    {
        var client = _factory.NewClient();

        using var login = await client.PostAsync("/login", new FormUrlEncodedContent([new("password", ShootoutFactory.Password)]), Token);

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_carry_a_strict_content_security_policy()
    {
        using var response = await _factory.NewClient().GetAsync("/login", Token);

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_changing_requests_from_another_site_are_refused()
    {
        var client = _factory.NewClient(signedIn: true);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/runs") { Content = JsonContent.Create(RandomVersusRandom) };
        foreign.Headers.Remove("Origin");
        foreign.Headers.Add("Origin", "https://evil.example");
        using var without = new HttpRequestMessage(HttpMethod.Post, "/api/runs") { Content = JsonContent.Create(RandomVersusRandom) };
        without.Headers.Add("Origin", "");

        using var first = await client.SendAsync(foreign, Token);

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        var bare = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var second = await bare.PostAsync("/login", new FormUrlEncodedContent([new("password", "x")]), Token);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task Too_many_failed_logins_are_throttled()
    {
        using var factory = new ShootoutFactory(loginAttempts: 2);
        var client = factory.NewClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.PostAsync("/login", new FormUrlEncodedContent([new("password", "wrong")]), Token);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public void A_short_password_stops_the_application_from_starting()
    {
        using var factory = new ShootoutFactory(password: "short");

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("chess", "random", "Unknown game")]
    [InlineData("split-or-steal", "scripted:/etc/passwd", "Scripted players")]
    [InlineData("split-or-steal", "nowhere:model", "Unknown endpoint")]
    [InlineData("split-or-steal", "", "missing")]
    public async Task A_bad_request_is_explained_in_plain_words(string game, string playerA, string expected)
    {
        var client = _factory.NewClient(signedIn: true);

        using var response = await client.PostAsJsonAsync("/api/runs", new { game, a = playerA, b = "random" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, (await JsonOf(response))["error"]!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5.5)]
    public async Task The_spending_limit_is_bounded(double limit)
    {
        var client = _factory.NewClient(signedIn: true);

        using var response = await client.PostAsJsonAsync("/api/runs", new { game = "split-or-steal", a = "random", b = "random", maxCostUsd = limit }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("spending limit", (await JsonOf(response))["error"]!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_match_streams_live_ends_and_lands_in_the_history()
    {
        var client = _factory.NewClient(signedIn: true);

        using var started = await client.PostAsJsonAsync("/api/runs", RandomVersusRandom, Token);
        var run = await JsonOf(started);
        using var stream = await client.GetAsync($"/api/runs/{run["id"]}/stream", Token);
        var body = await stream.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType!.MediaType);
        var lines = body.Split('\n').Where(l => l.StartsWith("data: {\"type\"", StringComparison.Ordinal)).ToArray();
        Assert.Contains("\"type\":\"match_started\"", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("\"type\":\"match_finished\"", StringComparison.Ordinal));
        Assert.Contains("event: end\ndata: finished", body, StringComparison.Ordinal);

        var name = run["match"]!.ToString();
        var history = await JsonOf(await client.GetAsync("/api/matches", Token));
        Assert.Equal(name, history[0]!["name"]!.ToString());
        Assert.Equal("random", history[0]!["playerA"]!.ToString());
        var events = await (await client.GetAsync($"/api/matches/{name}/events", Token)).Content.ReadAsStringAsync(Token);
        Assert.Equal(lines.Length, events.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        var report = await client.GetAsync($"/api/matches/{name}/report", Token);
        Assert.Equal("text/plain", report.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_match_can_be_downloaded_as_an_excel_workbook()
    {
        var client = _factory.NewClient(signedIn: true);
        var run = await JsonOf(await client.PostAsJsonAsync("/api/runs", RandomVersusRandom, Token));
        await (await client.GetAsync($"/api/runs/{run["id"]}/stream", Token)).Content.ReadAsStringAsync(Token);
        var name = run["match"]!.ToString();

        using var response = await client.GetAsync($"/api/matches/{name}/export.xlsx", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"{name}.xlsx", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        using var zip = new System.IO.Compression.ZipArchive(await response.Content.ReadAsStreamAsync(Token));
        var sheets = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, sheets.Count);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Assert.NotNull(System.Xml.Linq.XDocument.Parse(await reader.ReadToEndAsync(Token)));
        }

        var transcript = await new StreamReader(zip.GetEntry("xl/worksheets/sheet2.xml")!.Open()).ReadToEndAsync(Token);
        Assert.Contains("Private thought", transcript, StringComparison.Ordinal);
        Assert.Contains("Message", transcript, StringComparison.Ordinal);
        Assert.Contains("Move", transcript, StringComparison.Ordinal);
        var debrief = await new StreamReader(zip.GetEntry("xl/worksheets/sheet3.xml")!.Open()).ReadToEndAsync(Token);
        Assert.Contains("trust in opponent", debrief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_excel_export_needs_a_session_and_an_existing_match()
    {
        using var anonymous = await _factory.NewClient().GetAsync("/api/matches/x/export.xlsx", Token);
        using var missing = await _factory.NewClient(signedIn: true).GetAsync("/api/matches/does-not-exist/export.xlsx", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public void Text_from_models_is_cleaned_for_xml_and_columns_are_named_like_in_excel()
    {
        var cleaned = XlsxWriter.Clean("a\u0000b\u0008c\u000bd\tÄ\n😀\ud800");

        Assert.Equal("abcd\tÄ\n😀", cleaned);
        Assert.Equal(32000, XlsxWriter.Clean(new string('x', 40000)).Length);
        Assert.Equal(["A", "Z", "AA", "AZ", "BA"], new[] { 0, 25, 26, 51, 52 }.Select(XlsxWriter.Column));
    }

    [Fact]
    public async Task A_late_browser_gets_the_whole_match_and_can_resume_after_an_event()
    {
        var client = _factory.NewClient(signedIn: true);
        var run = await JsonOf(await client.PostAsJsonAsync("/api/runs", RandomVersusRandom, Token));
        var full = await (await client.GetAsync($"/api/runs/{run["id"]}/stream", Token)).Content.ReadAsStringAsync(Token);

        using var resume = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{run["id"]}/stream");
        resume.Headers.Add("Last-Event-ID", "2");
        var rest = await (await client.SendAsync(resume, Token)).Content.ReadAsStringAsync(Token);

        Assert.StartsWith("id: 0\n", full, StringComparison.Ordinal);
        Assert.StartsWith("id: 3\n", rest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_run_is_404()
    {
        using var response = await _factory.NewClient(signedIn: true).GetAsync("/api/runs/nope/stream", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    [InlineData("%2e%2e")]
    [InlineData(".hidden")]
    [InlineData("does-not-exist")]
    public async Task Match_names_cannot_leave_the_matches_folder(string name)
    {
        var client = _factory.NewClient(signedIn: true);

        using var events = await client.GetAsync($"/api/matches/{name}/events", Token);
        using var report = await client.GetAsync($"/api/matches/{name}/report", Token);

        Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
    }

    [Fact]
    public async Task The_number_of_matches_per_day_is_limited_and_survives_a_restart()
    {
        using var first = new ShootoutFactory(maxMatchesPerDay: 1);
        var client = first.NewClient(signedIn: true);
        var run = await JsonOf(await client.PostAsJsonAsync("/api/runs", RandomVersusRandom, Token));
        await (await client.GetAsync($"/api/runs/{run["id"]}/stream", Token)).Content.ReadAsStringAsync(Token);

        using var again = await client.PostAsJsonAsync("/api/runs", RandomVersusRandom, Token);
        using var restarted = new ShootoutFactory(maxMatchesPerDay: 1, root: first.Root);
        using var afterRestart = await restarted.NewClient(signedIn: true).PostAsJsonAsync("/api/runs", RandomVersusRandom, Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.Contains("matches per day", (await JsonOf(again))["error"]!.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.TooManyRequests, afterRestart.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_every_open_session()
    {
        using var first = new ShootoutFactory();
        using var login = await first.NewClient().PostAsync("/login", new FormUrlEncodedContent([new("password", ShootoutFactory.Password)]), Token);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var second = new ShootoutFactory(password: "a-completely-new-password", root: first.Root);
        var client = second.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        request.Headers.Add("Cookie", cookie);

        using var stillOpen = await first.NewClient().SendAsync(CloneWithCookie(cookie), Token);
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage CloneWithCookie(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    [Fact]
    public void The_archive_lists_only_directories_with_a_protocol()
    {
        Directory.CreateDirectory(Path.Combine(_factory.Matches, "empty"));
        Directory.CreateDirectory(Path.Combine(_factory.Matches, "real"));
        File.WriteAllText(Path.Combine(_factory.Matches, "real", "events.jsonl"), "{}\n");
        var archive = new MatchArchive(Options.Create(new WebOptions { MatchesDirectory = _factory.Matches }));

        var items = archive.List();

        var item = Assert.Single(items);
        Assert.Equal("real", item.Name);
        Assert.False(item.Complete);
        Assert.Null(archive.Resolve("empty/../real"));
        Assert.NotNull(archive.Resolve("real"));
    }
}

public sealed class WebRunTests : IDisposable
{
    private readonly ShootoutFactory _factory = new(blockingPlayers: true);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_one_match_runs_at_a_time_and_cancelling_ends_it_cleanly()
    {
        var client = _factory.NewClient(signedIn: true);
        var request = new { game = "split-or-steal", a = "slow", b = "slow", language = "en" };

        using var first = await client.PostAsJsonAsync("/api/runs", request, Token);
        using var second = await client.PostAsJsonAsync("/api/runs", request, Token);
        var run = JsonNode.Parse(await first.Content.ReadAsStringAsync(Token))!;
        await WaitUntilStartedAsync(client, run["match"]!.ToString());
        using var cancel = await client.PostAsync($"/api/runs/{run["id"]}/cancel", null, Token);
        var stream = await (await client.GetAsync($"/api/runs/{run["id"]}/stream", Token)).Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Contains("\"type\":\"match_aborted\"", stream, StringComparison.Ordinal);
        Assert.Contains("event: end\ndata: cancelled", stream, StringComparison.Ordinal);
        var current = JsonNode.Parse(await (await client.GetAsync("/api/runs/current", Token)).Content.ReadAsStringAsync(Token))!;
        Assert.Equal("cancelled", current["run"]!["status"]!.ToString());
        Assert.True(File.Exists(Path.Combine(Directory.GetDirectories(_factory.Matches)[0], "report.md")));
    }

    private static async Task WaitUntilStartedAsync(HttpClient client, string match)
    {
        for (var i = 0; i < 100; i++)
        {
            using var response = await client.GetAsync($"/api/matches/{match}/events", Token);
            if (response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(Token)).Contains("match_started", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        Assert.Fail("The match did not start.");
    }

    [Fact]
    public async Task The_stream_tells_the_page_who_is_being_waited_for()
    {
        var client = _factory.NewClient(signedIn: true);
        var run = JsonNode.Parse(await (await client.PostAsJsonAsync("/api/runs", new { game = "split-or-steal", a = "slow", b = "slow", language = "en" }, Token)).Content.ReadAsStringAsync(Token))!;
        await WaitUntilStartedAsync(client, run["match"]!.ToString());
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync($"/api/runs/{run["id"]}/stream", HttpCompletionOption.ResponseHeadersRead, limit.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(limit.Token));
        var seen = new System.Text.StringBuilder();

        string? progress = null;
        while (progress is null && await reader.ReadLineAsync(limit.Token) is { } line)
        {
            seen.AppendLine(line);
            if (line.StartsWith("data: {", StringComparison.Ordinal) && line.Contains("\"now\"", StringComparison.Ordinal))
            {
                progress = line;
            }
        }

        await client.PostAsync($"/api/runs/{run["id"]}/cancel", null, Token);
        Assert.NotNull(progress);
        Assert.Contains("event: progress", seen.ToString(), StringComparison.Ordinal);
        Assert.True(progress.Contains("\"A\"", StringComparison.Ordinal) || progress.Contains("\"B\"", StringComparison.Ordinal), progress);
    }

    [Fact]
    public async Task Cancelling_an_unknown_run_is_404()
    {
        using var response = await _factory.NewClient(signedIn: true).PostAsync("/api/runs/nope/cancel", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
