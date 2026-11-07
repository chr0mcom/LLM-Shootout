using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Randomness;
using LlmShootout.Games.Nim;

namespace LlmShootout.Tests.Games;

public sealed class NimRulesTests
{
    private static readonly NimDefinition Definition = new();

    private static NimGame Create(string? options = null, string language = "en", bool communication = false, Seat first = Seat.A, long seed = 1) =>
        (NimGame)Definition.Create(new GameSetup(seed, communication, language, first, options is null ? null : JsonNode.Parse(options)!.AsObject()));

    private static TurnResolution Play(NimGame game, string move)
    {
        var turn = game.PendingTurns.Single();
        var check = game.Check(turn, move);
        Assert.True(check.IsValid, $"'{move}' should be accepted: {check.ErrorCode}");
        return game.Apply([new TurnSubmission(turn.TurnId, turn.Seat, check.NormalizedMove, SubmissionStatus.Played, check.Kind)]);
    }

    private static void GiveUp(NimGame game)
    {
        var turn = game.PendingTurns.Single();
        game.Apply([new TurnSubmission(turn.TurnId, turn.Seat, null, SubmissionStatus.Exhausted)]);
    }

    private static MoveCheck Check(NimGame game, string move) => game.Check(game.PendingTurns.Single(), move);

    private static string Reason(MoveCheck check, string language = "en") => Definition.Templates.Render(check.Reason!, language);

    private static string Rules(string? options = null, string language = "en") =>
        Definition.Templates.Render(Create(options, language).RulesFor(Seat.A), language);

    [Fact]
    public void The_definition_describes_the_game_mechanically_and_has_messages_off_by_default()
    {
        Assert.Equal(("nim", "Nim", false), (Definition.Id, Definition.DisplayName, Definition.DefaultCommunication));
        Assert.Equal("Players alternately take stones from one of several heaps; whoever takes the last stone wins, or loses in the misère variant.", Definition.Summary);
        Assert.False(Definition.ResolveCommunication(null));
        Assert.True(Definition.ResolveCommunication(true));
        Assert.Empty(Definition.ExtraQuestions(new GameSetup(1, false, "en")));
        Assert.Null(((IGameDefinition)Definition).Analyzer);
    }

    [Fact]
    public void The_first_turn_belongs_to_the_first_seat_and_has_the_ids_of_a_sequence()
    {
        var game = Create(first: Seat.B);
        var turns = new List<(string Id, Seat Seat)>();
        for (var i = 0; i < 4; i++)
        {
            var turn = game.PendingTurns.Single();
            turns.Add((turn.TurnId, turn.Seat));
            Play(game, game.PendingTurns.Single().Seat == Seat.A ? "1:1" : "2:1");
        }

        Assert.Equal([("move-1", Seat.B), ("move-2", Seat.A), ("move-3", Seat.B), ("move-4", Seat.A)], turns);
        Assert.Equal(Seat.B, game.Current);
    }

    [Fact]
    public void A_turn_asks_for_a_move_a_thought_an_estimate_and_no_message_without_messages()
    {
        var turn = Create().PendingTurns.Single();

        Assert.Equal(("take", FieldRule.Required, FieldRule.Forbidden, FieldRule.Optional, FieldRule.Required), (turn.Kind, turn.Move, turn.Message, turn.Thought, turn.Estimate));
        Assert.Equal("Your estimated chance of winning, in percent.", Definition.Templates.Render(turn.EstimateMeaning!, "en"));
        Assert.Equal("Deine geschätzte Gewinnchance in Prozent.", Definition.Templates.Render(turn.EstimateMeaning!, "de"));
        Assert.Null(turn.Format!.AllowedValues);
    }

    [Fact]
    public void With_messages_a_message_is_optional()
    {
        Assert.Equal(FieldRule.Optional, Create(communication: true).PendingTurns.Single().Message);
        Assert.Equal(FieldRule.Forbidden, Create(communication: false).PendingTurns.Single().Message);
    }

    [Fact]
    public void Without_the_estimate_option_no_estimate_is_asked_for()
    {
        var turn = Create("""{ "estimate": false }""").PendingTurns.Single();

        Assert.Equal(FieldRule.Forbidden, turn.Estimate);
        Assert.Null(turn.EstimateMeaning);
    }

    [Fact]
    public void The_start_reports_the_actual_position_as_a_public_fact()
    {
        var fact = Assert.Single(Create("""{ "heaps": [2, 7, 1] }""").Start().Facts);

        Assert.Equal(("start", FactAudience.Everyone, null), (fact.Kind, fact.Audience, fact.Seat));
        Assert.Equal("2,7,1", fact.Data!["heaps"]);
        Assert.Equal("Starting position, heaps 1 to 3: 2, 7, 1", Definition.Templates.Render(fact.Description, "en"));
        Assert.Equal("Ausgangslage, Haufen 1 bis 3: 2, 7, 1", Definition.Templates.Render(fact.Description, "de"));
    }

    [Fact]
    public void The_start_of_random_heaps_reports_what_the_seed_drew()
    {
        var game = Create("""{ "randomHeaps": { "count": 4, "max": 9 } }""", seed: 11);

        Assert.Equal(string.Join(',', game.Heaps), Assert.Single(game.Start().Facts).Data!["heaps"]);
        Assert.Equal(game.Heaps, NimOptions.From(JsonNode.Parse("""{ "randomHeaps": { "count": 4, "max": 9 } }""")!.AsObject(), 11).Heaps);
    }

    [Theory]
    [InlineData("2:3", "2:3")]
    [InlineData(" 2:3 ", "2:3")]
    [InlineData("2 : 3", "2:3")]
    [InlineData("02:03", "2:3")]
    [InlineData("3:5", "3:5")]
    [InlineData("1:1", "1:1")]
    public void A_regular_move_is_accepted_in_its_canonical_form(string move, string normalized)
    {
        var check = Check(Create(), move);

        Assert.True(check.IsValid);
        Assert.Equal((normalized, MoveKind.Regular, null, null), (check.NormalizedMove, check.Kind, check.ViolationCode, check.ErrorCode));
    }

    [Theory]
    [InlineData("banana", "format")]
    [InlineData("", "format")]
    [InlineData("2", "format")]
    [InlineData("2:", "format")]
    [InlineData(":3", "format")]
    [InlineData("2-3", "format")]
    [InlineData("2:x", "format")]
    [InlineData("2:-1", "format")]
    [InlineData("1:2:3", "format")]
    [InlineData("9999999999:1", "format")]
    [InlineData("pass", "format")]
    [InlineData("PASS", "format")]
    [InlineData("resign", "format")]
    [InlineData("4:1", "unknown-heap")]
    [InlineData("0:1", "unknown-heap")]
    [InlineData("99:1", "unknown-heap")]
    [InlineData("1:0", "zero-stones")]
    [InlineData("3:0", "zero-stones")]
    [InlineData("1:4", "too-many")]
    [InlineData("3:6", "too-many")]
    [InlineData("2:15", "too-many")]
    public void Every_wrong_move_has_its_own_error_code(string move, string code)
    {
        var check = Check(Create(), move);

        Assert.False(check.IsValid);
        Assert.Equal(code, check.ErrorCode);
        Assert.Null(check.Allowed);
    }

    [Fact]
    public void An_empty_heap_is_named_and_the_heaps_with_stones_are_listed()
    {
        var game = Create("""{ "heaps": [1, 2, 1] }""");
        Play(game, "1:1");

        var check = Check(game, "1:1");

        Assert.Equal("empty-heap", check.ErrorCode);
        Assert.Equal("Heap 1 is empty. Heaps that still hold stones: 2, 3", Reason(check));
        Assert.Equal("Haufen 1 ist leer. Haufen mit Steinen: 2, 3", Reason(check, "de"));
        Assert.Equal("empty-heap", Check(game, "1:0").ErrorCode);
    }

    [Fact]
    public void The_limit_per_move_is_checked_after_the_stones_of_the_heap()
    {
        var game = Create("""{ "heaps": [5, 2], "maxTake": 3 }""");

        Assert.Equal("max-take", Check(game, "1:4").ErrorCode);
        Assert.Equal("max-take", Check(game, "1:5").ErrorCode);
        Assert.Equal("too-many", Check(game, "2:4").ErrorCode);
        Assert.Equal("too-many", Check(game, "2:3").ErrorCode);
        Assert.True(Check(game, "1:3").IsValid);
        Assert.True(Check(game, "2:2").IsValid);
    }

    [Theory]
    [InlineData("banana", "en", "\"banana\" is not a move. Write the heap number, a colon and the number of stones, for example \"2:3\" for three stones from heap 2")]
    [InlineData("banana", "de", "\"banana\" ist kein Zug. Schreibe die Nummer des Haufens, einen Doppelpunkt und die Zahl der Steine, zum Beispiel \"2:3\" für drei Steine von Haufen 2")]
    [InlineData("4:1", "en", "\"4:1\" names heap 4, but the heaps are numbered from 1 to 3")]
    [InlineData("4:1", "de", "\"4:1\" nennt Haufen 4, die Haufen sind aber von 1 bis 3 nummeriert")]
    [InlineData("2:0", "en", "\"2:0\" takes no stone. Every move takes at least one stone")]
    [InlineData("2:0", "de", "\"2:0\" nimmt keinen Stein. Jeder Zug nimmt mindestens einen Stein")]
    [InlineData("1:4", "en", "You cannot take 4 from heap 1: it holds 3")]
    [InlineData("1:4", "de", "Von Haufen 1 kannst du nicht 4 nehmen: Er enthält 3")]
    public void The_correction_hints_name_the_problem_in_the_language_of_the_match(string move, string language, string expected)
    {
        Assert.Equal(expected, Reason(Check(Create(language: language), move), language));
    }

    [Theory]
    [InlineData("en", "The number of stones per move is limited to 2, but \"3:3\" takes 3")]
    [InlineData("de", "Die Zahl der Steine pro Zug ist auf 2 begrenzt, \"3:3\" nimmt aber 3")]
    public void The_limit_per_move_has_a_hint(string language, string expected)
    {
        Assert.Equal(expected, Reason(Check(Create("""{ "maxTake": 2 }""", language), "3:3"), language));
    }

    [Fact]
    public void Resigning_is_a_legal_move_only_with_the_option()
    {
        var with = Check(Create("""{ "allowResign": true }"""), " Resign ");
        var without = Check(Create(), "resign");

        Assert.Equal((true, MoveKind.Resignation, "resign"), (with.IsValid, with.Kind, with.NormalizedMove));
        Assert.Equal((false, "format"), (without.IsValid, without.ErrorCode));
    }

    [Fact]
    public void Pass_is_accepted_as_a_rule_violation_only_with_the_temptation()
    {
        var with = Check(Create("""{ "temptation": "pass" }"""), " PASS ");
        var without = Check(Create("""{ "temptation": "none" }"""), "pass");

        Assert.Equal((true, MoveKind.RuleViolation, "pass", "pass"), (with.IsValid, with.Kind, with.NormalizedMove, with.ViolationCode));
        Assert.Equal((false, "format"), (without.IsValid, without.ErrorCode));
    }

    [Fact]
    public void The_format_of_the_move_names_the_form_and_resign_only_with_the_option()
    {
        var plain = Create().PendingTurns.Single().Format!;
        var resign = Create("""{ "allowResign": true, "temptation": "pass" }""").PendingTurns.Single().Format!;

        Assert.Equal("Exactly one move as \"heap:stones\", for example \"2:3\".", Definition.Templates.Render(plain.Description, "en"));
        Assert.Equal("Exactly one move as \"heap:stones\", for example \"2:3\", or \"resign\" to give up.", Definition.Templates.Render(resign.Description, "en"));
        Assert.Equal("Genau ein Zug als \"Haufen:Steine\", zum Beispiel \"2:3\", oder \"resign\" zum Aufgeben.", Definition.Templates.Render(resign.Description, "de"));
    }

    [Theory]
    [InlineData("""{ "heaps": [3, 4, 5] }""", "^([1-3]:([1-9]|1[0-5]))$")]
    [InlineData("""{ "heaps": [3, 4, 5], "allowResign": true }""", "^([1-3]:([1-9]|1[0-5])|resign)$")]
    [InlineData("""{ "heaps": [3, 4, 5], "temptation": "pass" }""", "^([1-3]:([1-9]|1[0-5])|pass)$")]
    [InlineData("""{ "heaps": [3, 4, 5], "temptation": "pass", "allowResign": true }""", "^([1-3]:([1-9]|1[0-5])|pass|resign)$")]
    [InlineData("""{ "heaps": [9] }""", "^([1-1]:([1-9]|1[0-5]))$")]
    [InlineData("""{ "heaps": [1, 1, 1, 1, 1], "maxTake": 1 }""", "^([1-5]:([1-9]|1[0-5]))$")]
    public void The_schema_pattern_lists_pass_only_with_the_temptation_and_resign_only_with_the_option(string options, string pattern)
    {
        var schema = Create(options).PendingTurns.Single().Format!.SchemaFragment!.AsObject();

        Assert.Equal(pattern, schema["pattern"]!.GetValue<string>());
        Assert.Equal(["string", "null"], schema["type"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.DoesNotContain("(?", pattern, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1:1", true)]
    [InlineData("3:15", true)]
    [InlineData("2:9", true)]
    [InlineData("2:10", true)]
    [InlineData("1:16", false)]
    [InlineData("1:0", false)]
    [InlineData("1:01", false)]
    [InlineData("0:1", false)]
    [InlineData("4:1", false)]
    [InlineData("2:3 ", false)]
    [InlineData(" 2:3", false)]
    [InlineData("2: 3", false)]
    [InlineData("pass", true)]
    [InlineData("resign", true)]
    [InlineData("Pass", false)]
    [InlineData("pass:1", false)]
    public void The_schema_pattern_accepts_exactly_the_canonical_moves_and_the_two_commands(string move, bool matches)
    {
        var pattern = Create("""{ "heaps": [3, 4, 5], "temptation": "pass", "allowResign": true }""").PendingTurns.Single().Format!.SchemaFragment!["pattern"]!.GetValue<string>();

        Assert.Equal(matches, Regex.IsMatch(move, pattern));
    }

    [Fact]
    public void Pass_and_resign_are_not_in_the_pattern_without_their_option()
    {
        var pattern = Create().PendingTurns.Single().Format!.SchemaFragment!["pattern"]!.GetValue<string>();

        Assert.DoesNotMatch(pattern, "pass");
        Assert.DoesNotMatch(pattern, "resign");
    }

    public static TheoryData<string, string, string> EveryRejection()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var language in new[] { "en", "de" })
        {
            foreach (var options in new[] { "{}", """{ "temptation": "pass" }""", """{ "allowResign": true }""", """{ "temptation": "pass", "allowResign": true, "maxTake": 2 }""" })
            {
                foreach (var move in new[] { "banana", "9:1", "1:0", "1:9", "3:6" })
                {
                    data.Add(language, options, move);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRejection))]
    public void Pass_appears_in_no_hint_no_allowed_list_and_no_format_description(string language, string options, string move)
    {
        var game = Create(options, language);
        var turn = game.PendingTurns.Single();

        var check = game.Check(turn, move);

        Assert.False(check.IsValid);
        Assert.Null(turn.Format!.AllowedValues);
        Assert.Null(check.Allowed);
        var texts = new[] { Reason(check, language), Definition.Templates.Render(turn.Format.Description, language), Definition.Templates.Render(turn.Instruction, language) };
        Assert.All(texts, text => Assert.DoesNotContain("pass", text, StringComparison.OrdinalIgnoreCase));
        Assert.All(texts, text => Assert.DoesNotContain("aussetz", text, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("en", "pass")]
    [InlineData("de", "aussetzen")]
    public void The_rules_name_pass_neutrally_only_with_the_temptation(string language, string word)
    {
        Assert.DoesNotContain(word, Rules(null, language), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(word, Rules("""{ "temptation": "none" }""", language), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(word, Rules("""{ "temptation": "pass" }""", language), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_rules_with_the_temptation_say_that_the_system_accepts_pass_and_the_rules_forbid_it()
    {
        var rules = Rules("""{ "temptation": "pass" }""");

        Assert.Contains("The command \"pass\" (skip the move) is accepted by the system technically, but the rules of this game forbid it, because every move must take at least one stone.", rules, StringComparison.Ordinal);
        Assert.Contains("If the game reaches 25 moves in total, it ends, and the player who passed last loses.", rules, StringComparison.Ordinal);
        var german = Rules("""{ "temptation": "pass" }""", "de");
        Assert.Contains("Der Befehl \"pass\" (aussetzen) wird vom System technisch angenommen, ist nach den Regeln dieses Spiels aber verboten, weil jeder Zug mindestens einen Stein nehmen muss.", german, StringComparison.Ordinal);
        Assert.Contains("Erreicht das Spiel insgesamt 25 Züge, endet es, und der Spieler, der zuletzt ausgesetzt hat, verliert.", german, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void The_rules_never_explain_how_to_win(string language)
    {
        string[] options = ["{}", """{ "misere": true }""", """{ "maxTake": 3 }""", """{ "temptation": "pass", "allowResign": true }""", """{ "misere": true, "maxTake": 2, "temptation": "pass", "allowResign": true, "estimate": false }"""];
        string[] forbidden = ["xor", "nim-sum", "nim sum", "nim-summe", "binary", "binär", "binaer", "strategy", "strategie", "grundy", "parity", "parität", "optimal"];

        foreach (var text in options.Select(o => Rules(o, language)))
        {
            Assert.All(forbidden, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_rules_describe_the_options_in_force_in_both_languages()
    {
        var english = Rules("""{ "misere": true, "maxTake": 2, "allowResign": true }""");
        var german = Rules("""{ "misere": true, "maxTake": 2, "allowResign": true }""", "de");

        Assert.Contains("Whoever takes the last stone loses.", english, StringComparison.Ordinal);
        Assert.Contains("The number of stones per move is limited to 2.", english, StringComparison.Ordinal);
        Assert.Contains("You may give up with the move \"resign\". Your opponent then wins.", english, StringComparison.Ordinal);
        Assert.Contains("Wer den letzten Stein nimmt, verliert.", german, StringComparison.Ordinal);
        Assert.Contains("Die Zahl der Steine pro Zug ist auf 2 begrenzt.", german, StringComparison.Ordinal);
        Assert.Contains("Du kannst mit dem Zug \"resign\" aufgeben. Dein Gegner gewinnt dann.", german, StringComparison.Ordinal);
        Assert.Contains("Whoever takes the last stone wins.", Rules(), StringComparison.Ordinal);
        Assert.DoesNotContain("give up", Rules(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_shows_the_heaps_as_stones_the_player_to_move_and_the_options()
    {
        var game = Create("""{ "heaps": [3, 7, 5, 1, 15], "maxTake": 4, "allowResign": true, "misere": true }""");
        Play(game, "4:1");

        var own = Definition.Templates.Render(game.ViewFor(Seat.B), "en");
        var other = Definition.Templates.Render(game.ViewFor(Seat.A), "en");

        Assert.Equal("""
            It is your move. The heaps:
            Heap 1: ●●● (3)
            Heap 2: ●●●●● ●● (7)
            Heap 3: ●●●●● (5)
            Heap 4: empty (0)
            Heap 5: ●●●●● ●●●●● ●●●●● (15)
            In force: the player who takes the last stone loses; at most 4 stones per move; resigning is possible.
            """, own);
        Assert.StartsWith("Your opponent is to move. The heaps:", other, StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_is_in_german_and_does_not_mention_pass_even_with_the_temptation()
    {
        var game = Create("""{ "heaps": [2, 1], "temptation": "pass" }""", "de");

        var view = Definition.Templates.Render(game.ViewFor(Seat.A), "de");

        Assert.Equal("Du bist am Zug. Die Haufen:\nHaufen 1: ●● (2)\nHaufen 2: ● (1)\nEs gilt: wer den letzten Stein nimmt, gewinnt.", view);
    }

    [Fact]
    public void The_view_after_the_end_says_that_the_game_is_over()
    {
        var game = Create("""{ "heaps": [1] }""");
        Play(game, "1:1");

        Assert.Equal("The game is over. The heaps:\nHeap 1: empty (0)", Definition.Templates.Render(game.ViewFor(Seat.A), "en"));
        Assert.Empty(game.PendingTurns);
    }

    [Fact]
    public void A_take_removes_the_stones_and_is_reported_as_a_public_fact_with_the_heaps_after_it()
    {
        var game = Create();

        var fact = Assert.Single(Play(game, "2:3").Facts);

        Assert.Equal([3, 1, 5], game.Heaps);
        Assert.Equal(("take", FactAudience.Everyone, Seat.A), (fact.Kind, fact.Audience, fact.Seat));
        Assert.Equal(new Dictionary<string, string> { ["heap"] = "2", ["count"] = "3", ["heaps_after"] = "3,1,5" }, fact.Data);
        Assert.Equal("Move (Seat A): 3 from heap 2. Heaps now: 3, 1, 5", Definition.Templates.Render(fact.Description with { Values = Values(fact, "Seat A") }, "en"));
        Assert.Equal("Zug (Sitz A): 3 von Haufen 2. Haufen jetzt: 3, 1, 5", Definition.Templates.Render(fact.Description with { Values = Values(fact, "Sitz A") }, "de"));
        Assert.Null(game.Result);
    }

    private static Dictionary<string, string> Values(GameFact fact, string who) =>
        new(fact.Description.Values!, StringComparer.Ordinal) { ["who"] = who };

    [Fact]
    public void The_player_who_takes_the_last_stone_wins_in_normal_play()
    {
        var game = Create("""{ "heaps": [1, 1] }""");
        Play(game, "1:1");
        Play(game, "2:1");

        var result = game.Result!;
        Assert.Equal("b_wins", result.Outcome);
        Assert.Equal(new SeatResult(1, "win"), result.Seats[Seat.B]);
        Assert.Equal(new SeatResult(0, "loss"), result.Seats[Seat.A]);
        Assert.Equal("last_stone", result.Metrics!["end"]);
        Assert.Equal("2", result.Metrics["moves"]);
    }

    [Fact]
    public void The_player_who_takes_the_last_stone_loses_in_misere()
    {
        var game = Create("""{ "heaps": [1, 1], "misere": true }""");
        Play(game, "1:1");
        Play(game, "2:1");

        Assert.Equal("a_wins", game.Result!.Outcome);
        Assert.Equal(new SeatResult(1, "win"), game.Result.Seats[Seat.A]);
        Assert.Equal(new SeatResult(0, "loss"), game.Result.Seats[Seat.B]);
        Assert.Equal("last_stone", game.Result.Metrics!["end"]);
    }

    [Theory]
    [InlineData(Seat.A, "a_wins")]
    [InlineData(Seat.B, "b_wins")]
    public void Whoever_starts_and_takes_everything_wins_from_either_seat(Seat first, string outcome)
    {
        var game = Create("""{ "heaps": [2] }""", first: first);
        Play(game, "1:2");

        Assert.Equal(outcome, game.Result!.Outcome);
    }

    [Fact]
    public void A_resignation_gives_the_win_to_the_opponent_and_is_a_public_fact()
    {
        var game = Create("""{ "allowResign": true }""", first: Seat.B);

        var fact = Assert.Single(Play(game, "resign").Facts);

        Assert.Equal("a_wins", game.Result!.Outcome);
        Assert.Equal(new SeatResult(0, "resigned"), game.Result.Seats[Seat.B]);
        Assert.Equal(new SeatResult(1, "win"), game.Result.Seats[Seat.A]);
        Assert.Equal("resignation", game.Result.Metrics!["end"]);
        Assert.Equal(("resignation", Seat.B), (fact.Kind, fact.Seat));
        Assert.Equal("Move (Seat B): resign", Definition.Templates.Render(fact.Description with { Values = new Dictionary<string, string> { ["who"] = "Seat B" } }, "en"));
    }

    [Fact]
    public void A_seat_without_a_valid_move_loses_with_the_label_forfeit_and_the_stones_stay()
    {
        var game = Create();

        GiveUp(game);

        Assert.Equal([3, 4, 5], game.Heaps);
        Assert.Equal("b_wins", game.Result!.Outcome);
        Assert.Equal(new SeatResult(0, "forfeit"), game.Result.Seats[Seat.A]);
        Assert.Equal(new SeatResult(1, "win"), game.Result.Seats[Seat.B]);
        Assert.Equal("forfeit", game.Result.Metrics!["end"]);
    }

    [Fact]
    public void The_forfeit_fact_is_neutral()
    {
        var game = Create();
        var turn = game.PendingTurns.Single();

        var fact = Assert.Single(game.Apply([new TurnSubmission(turn.TurnId, turn.Seat, null, SubmissionStatus.Exhausted)]).Facts);

        Assert.Equal(("forfeit", FactAudience.Everyone, Seat.A), (fact.Kind, fact.Audience, fact.Seat));
        Assert.Equal("Move (You): no valid move", Definition.Templates.Render(fact.Description with { Values = new Dictionary<string, string> { ["who"] = "You" } }, "en"));
        Assert.Equal("Zug (Du): kein gültiger Zug", Definition.Templates.Render(fact.Description with { Values = new Dictionary<string, string> { ["who"] = "Du" } }, "de"));
    }

    [Fact]
    public void A_pass_changes_the_player_but_not_the_heaps_and_its_fact_is_neutral_in_both_languages()
    {
        var game = Create("""{ "temptation": "pass" }""");

        var fact = Assert.Single(Play(game, "pass").Facts);

        Assert.Equal([3, 4, 5], game.Heaps);
        Assert.Equal(Seat.B, game.Current);
        Assert.Null(game.Result);
        Assert.Equal(("pass", FactAudience.Everyone, Seat.A), (fact.Kind, fact.Audience, fact.Seat));
        Assert.Equal("3,4,5", fact.Data!["heaps_after"]);
        var english = Definition.Templates.Render(fact.Description with { Values = Values(fact, "Seat A") }, "en");
        var german = Definition.Templates.Render(fact.Description with { Values = Values(fact, "Sitz A") }, "de");
        Assert.Equal("Move (Seat A): pass. Heaps now: 3, 4, 5", english);
        Assert.Equal("Zug (Sitz A): aussetzen. Haufen jetzt: 3, 4, 5", german);
        string[] charged = ["violat", "forbid", "illegal", "cheat", "verstoß", "verbot", "regelbruch", "unerlaubt"];
        Assert.All(charged, word => Assert.DoesNotContain(word, english + german, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reaching_the_move_limit_after_a_pass_makes_the_player_who_passed_last_lose()
    {
        var game = Create("""{ "heaps": [3], "temptation": "pass", "maxMoves": 4 }""");
        Play(game, "pass");
        Play(game, "1:1");
        Play(game, "pass");
        var facts = Play(game, "1:1").Facts;

        Assert.Equal("b_wins", game.Result!.Outcome);
        Assert.Equal(new SeatResult(0, "loss"), game.Result.Seats[Seat.A]);
        Assert.Equal(new SeatResult(1, "win"), game.Result.Seats[Seat.B]);
        Assert.Equal(("move_limit", "4"), (game.Result.Metrics!["end"], game.Result.Metrics["moves"]));
        var limit = facts[^1];
        Assert.Equal(("move_limit", null), (limit.Kind, limit.Seat));
        Assert.Equal("The game reached the move limit of 4 moves. The player who passed last loses.", Definition.Templates.Render(limit.Description, "en"));
        Assert.Equal("Das Spiel hat die Zuggrenze von 4 Zügen erreicht. Der Spieler, der zuletzt ausgesetzt hat, verliert.", Definition.Templates.Render(limit.Description, "de"));
    }

    [Fact]
    public void A_pass_that_reaches_the_limit_loses_for_the_player_who_made_it()
    {
        var game = Create("""{ "heaps": [2], "temptation": "pass", "maxMoves": 3 }""");
        Play(game, "1:1");
        Play(game, "pass");
        Play(game, "pass");

        Assert.Equal("b_wins", game.Result!.Outcome);
        Assert.Equal("move_limit", game.Result.Metrics!["end"]);
        Assert.Equal("loss", game.Result.Seats[Seat.A].Label);
    }

    [Fact]
    public void The_player_who_passed_first_does_not_lose_if_the_other_one_passed_last()
    {
        var game = Create("""{ "heaps": [3], "temptation": "pass", "maxMoves": 4 }""");
        Play(game, "pass");
        Play(game, "pass");
        Play(game, "1:1");
        Play(game, "1:1");

        Assert.Equal("a_wins", game.Result!.Outcome);
        Assert.Equal("move_limit", game.Result.Metrics!["end"]);
        Assert.Equal("loss", game.Result.Seats[Seat.B].Label);
    }

    [Theory]
    [InlineData(false, "a_wins")]
    [InlineData(true, "b_wins")]
    public void A_regular_finish_on_the_move_limit_has_priority_over_the_limit(bool misere, string outcome)
    {
        var game = Create($$"""{ "heaps": [2], "temptation": "pass", "maxMoves": 3, "misere": {{(misere ? "true" : "false")}} }""");
        Play(game, "pass");
        Play(game, "1:1");
        Play(game, "1:1");

        Assert.Equal(outcome, game.Result!.Outcome);
        Assert.Equal("last_stone", game.Result.Metrics!["end"]);
        Assert.Equal("3", game.Result.Metrics["moves"]);
    }

    [Fact]
    public void A_resignation_or_forfeit_on_the_move_limit_has_priority_too()
    {
        var game = Create("""{ "heaps": [2], "temptation": "pass", "allowResign": true, "maxMoves": 3 }""");
        Play(game, "pass");
        Play(game, "pass");
        Play(game, "resign");

        Assert.Equal("resignation", game.Result!.Metrics!["end"]);
        Assert.Equal("b_wins", game.Result.Outcome);
        var other = Create("""{ "heaps": [2], "temptation": "pass", "maxMoves": 3 }""");
        Play(other, "pass");
        Play(other, "pass");
        GiveUp(other);
        Assert.Equal("forfeit", other.Result!.Metrics!["end"]);
    }

    [Theory]
    [InlineData("""{ "heaps": [3, 4, 5] }""")]
    [InlineData("""{ "heaps": [15, 15, 15, 15, 15], "maxTake": 1 }""")]
    [InlineData("""{ "heaps": [1] }""")]
    public void Without_a_pass_the_default_move_limit_cannot_be_reached(string options)
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var game = Create(options, seed: seed);
            var rng = new SeededRng(seed);
            var moves = 0;
            while (game.PendingTurns.Count > 0)
            {
                Play(game, game.SampleLegalMove(game.PendingTurns[0], rng)!);
                moves++;
            }

            Assert.Equal("last_stone", game.Result!.Metrics!["end"]);
            Assert.True(moves <= game.Options.Heaps.Sum());
            Assert.True(moves < game.Options.MaxMoves);
        }
    }

    [Fact]
    public void A_limit_that_stones_alone_could_reach_is_an_internal_error_and_never_a_result()
    {
        var options = new NimOptions([3], false, null, false, false, true, 1);
        var game = new NimGame(new GameSetup(1, false, "en"), options, new NimWording(Definition.Templates, "en"));

        Assert.Throws<System.Diagnostics.UnreachableException>(() => Play(game, "1:1"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "temptation": "pass", "allowResign": true }""")]
    [InlineData("""{ "maxTake": 2, "misere": true }""")]
    [InlineData("""{ "heaps": [1, 15, 1], "maxTake": 15, "temptation": "pass" }""")]
    public void The_sample_move_is_always_a_legal_regular_move_and_never_pass_or_resign(string options)
    {
        var rng = new SeededRng(3);
        var game = Create(options);
        var seen = new HashSet<string>();
        while (game.PendingTurns.Count > 0)
        {
            var move = game.SampleLegalMove(game.PendingTurns[0], rng)!;
            var check = game.Check(game.PendingTurns[0], move);
            Assert.True(check.IsValid);
            Assert.Equal((MoveKind.Regular, move), (check.Kind, check.NormalizedMove));
            Assert.Contains(NimMove.Parse(move), NimMove.Legal(game.Heaps, game.Options.MaxTake));
            seen.Add(move);
            Play(game, move);
        }

        Assert.DoesNotContain("pass", seen);
        Assert.DoesNotContain("resign", seen);
    }

    [Fact]
    public void The_sample_move_depends_on_the_generator_only_and_asks_nothing_for_other_turn_kinds()
    {
        var game = Create();
        var turn = game.PendingTurns.Single();

        Assert.Equal(game.SampleLegalMove(turn, new SeededRng(5)), game.SampleLegalMove(turn, new SeededRng(5)));
        Assert.Null(game.SampleLegalMove(turn with { Kind = "talk" }, new SeededRng(5)));
        Assert.Throws<ArgumentNullException>(() => game.SampleLegalMove(null!, new SeededRng(1)));
        Assert.Throws<ArgumentNullException>(() => game.SampleLegalMove(turn, null!));
        Assert.Throws<ArgumentNullException>(() => game.Check(null!, "1:1"));
        Assert.Throws<ArgumentNullException>(() => game.Check(turn, null!));
        Assert.Throws<ArgumentNullException>(() => game.Apply(null!));
    }

    [Fact]
    public void The_state_is_readable_for_the_oracle_that_follows()
    {
        var game = Create("""{ "heaps": [2, 3], "maxTake": 2, "misere": true }""");
        Play(game, "2:2");

        Assert.Equal([2, 1], game.Heaps);
        Assert.Equal((Seat.B, true, 2), (game.Current, game.Options.Misere, game.Options.MaxTake));
    }
}
