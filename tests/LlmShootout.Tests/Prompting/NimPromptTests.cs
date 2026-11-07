using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LlmShootout.Arena;
using LlmShootout.Arena.Players;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Prompting;

public sealed partial class NimPromptTests : IDisposable
{
    private readonly MatchHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string Render(IEnumerable<PromptMessage> messages) =>
        string.Join("\n\n", messages.Select(m => $"=== {m.Role} ===\n{m.Text}"));

    private async Task PlayAsync(ScriptedPlayer a, ScriptedPlayer b, JsonObject? options, string language) =>
        await _harness.RunWithTokenAsync(new NimDefinition(), a, b, false, new MatchOptions { Debrief = false }, 7, options, LlmShootout.Arena.Games.Seat.A, TestContext.Current.CancellationToken, language);

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_prompt_of_the_standard_game_with_the_estimate_stays_stable(string language)
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "2:3", estimate: 50));

        await PlayAsync(a, new ScriptedPlayer(), null, language);

        Snapshot.Verify($"prompt-nim-estimate.{language}.txt", Render(a.Calls[0].Messages));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task The_prompts_with_the_temptation_and_all_other_options_stay_stable(string language)
    {
        var options = new JsonObject { ["temptation"] = "pass", ["allowResign"] = true, ["misere"] = true, ["maxTake"] = 3, ["estimate"] = false };
        var a = new ScriptedPlayer()
            .Reply(MatchHarness.Answer(move: "9:9"))
            .Reply(MatchHarness.Answer(move: "1:1"))
            .Reply(MatchHarness.Answer(move: "resign"));
        var b = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "pass"));

        await PlayAsync(a, b, options, language);

        Snapshot.Verify($"prompt-nim-temptation-first.{language}.txt", Render(a.Calls[0].Messages));
        Snapshot.Verify($"prompt-nim-temptation-retry.{language}.txt", Render(a.Calls[1].Messages));
        Snapshot.Verify($"prompt-nim-temptation-history.{language}.txt", Render(a.Calls[2].Messages));
    }

    [Fact]
    public async Task The_rules_of_a_game_without_the_temptation_do_not_contain_the_move_limit()
    {
        var a = new ScriptedPlayer().Reply(MatchHarness.Answer(move: "2:3", estimate: 50));

        await PlayAsync(a, new ScriptedPlayer(), null, "en");

        Assert.DoesNotContain("move limit", a.Calls[0].Messages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("moves in total", a.Calls[0].Messages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_languages_have_the_same_templates_with_the_same_placeholders()
    {
        var english = Sections("en");
        var german = Sections("de");

        Assert.Equal(english.Keys.Order(), german.Keys.Order());
        Assert.All(english, pair => Assert.Equal(Placeholders(pair.Value), Placeholders(german[pair.Key])));
    }

    private static Dictionary<string, string> Sections(string language)
    {
        var path = Path.Combine(Snapshot.RepositoryRoot, "src", "LlmShootout.Games", "Nim", "Prompts", $"nim.{language}.md");
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                key = line[3..].Trim();
                sections[key] = string.Empty;
            }
            else if (key is not null)
            {
                sections[key] += line + "\n";
            }
        }

        return sections;
    }

    private static string[] Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order()];

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.]*)\}")]
    private static partial Regex PlaceholderPattern();
}
