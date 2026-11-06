using System.Globalization;
using LlmShootout.Arena.Debriefing;
using LlmShootout.Arena.Games;
using LlmShootout.Arena.Prompting;

namespace LlmShootout.Arena.Answers;

/// <summary>How an answer is classified.</summary>
public enum AnswerClass
{
    /// <summary>Format and move are fine.</summary>
    Valid,

    /// <summary>No parsable JSON, a field is missing or forbidden, a message is too long, or the move breaks the rules.</summary>
    Invalid,

    /// <summary>No usable move, and the text declines or moralizes.</summary>
    Refusal,
}

/// <summary>Result of evaluating one answer against the rules of a turn.</summary>
public sealed record Evaluation(
    AnswerClass Class,
    string? Thought,
    bool ThoughtTruncated,
    string? Message,
    bool MessageTruncated,
    bool Silent,
    string? Move,
    IReadOnlyList<FieldViolation> Violations,
    IReadOnlyList<string> ExtraKeys)
{
    /// <summary>True if the answer can be used as it is.</summary>
    public bool IsValid => Class == AnswerClass.Valid;
}

/// <summary>Checks a parsed answer against the field rules of a turn and the rules of the game.</summary>
public sealed class AnswerEvaluator(MatchOptions options, PromptBuilder prompts)
{
    /// <summary>
    /// Evaluates an answer. In the <paramref name="isLastAttempt"/> the evaluation salvages what it can: a long
    /// message is shortened, a missing required message becomes silence, a failing optional move is dropped.
    /// </summary>
    public Evaluation Evaluate(TurnRequest turn, ParsedAnswer answer, string? rawText, IGame game, bool isLastAttempt)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(game);

        var violations = new List<FieldViolation>();
        var (thought, thoughtCut) = CheckThought(turn, answer, violations, isLastAttempt);
        var (message, messageCut, silent) = CheckMessage(turn, answer, violations, isLastAttempt);
        var move = CheckMove(turn, answer, game, violations, isLastAttempt);

        var cls = !answer.HasJson || violations.Count > 0 ? ClassifyFailure(rawText) : AnswerClass.Valid;
        return new Evaluation(cls, thought, thoughtCut, message, messageCut, silent, move, violations, answer.ExtraKeys);
    }

    private AnswerClass ClassifyFailure(string? rawText) =>
        RefusalDetector.IsRefusal(rawText, options.RefusalKeywords) ? AnswerClass.Refusal : AnswerClass.Invalid;

    private (string? Text, bool Truncated) CheckThought(TurnRequest turn, ParsedAnswer answer, List<FieldViolation> violations, bool last)
    {
        if (!answer.HasJson)
        {
            return (null, false);
        }

        var text = answer.Thought;
        if (!last)
        {
            AddPresenceViolation("thought", turn.Thought, text, violations);
        }

        return text is null || turn.Thought == FieldRule.Forbidden ? (null, false) : Shorten(text, options.MaxThoughtWords);
    }

    private (string? Text, bool Truncated, bool Silent) CheckMessage(TurnRequest turn, ParsedAnswer answer, List<FieldViolation> violations, bool last)
    {
        if (!answer.HasJson)
        {
            return (null, false, false);
        }

        var text = answer.Message;
        if (turn.Message == FieldRule.Forbidden)
        {
            AddPresenceViolation("message", turn.Message, text, violations);
            return (null, false, false);
        }

        if (text is null)
        {
            var silent = turn.Message == FieldRule.Required && last;
            if (turn.Message == FieldRule.Required && !last)
            {
                AddPresenceViolation("message", turn.Message, text, violations);
            }

            return (null, false, silent);
        }

        if (CountWords(text) <= options.MaxMessageWords)
        {
            return (text, false, false);
        }

        if (last)
        {
            var (cut, _) = Shorten(text, options.MaxMessageWords);
            return (cut, true, false);
        }

        violations.Add(new FieldViolation(
            "message",
            prompts.Arena("violation.too_long", ("count", CountWords(text).ToString(CultureInfo.InvariantCulture))),
            prompts.Arena("allowed.max_words", ("max", options.MaxMessageWords.ToString(CultureInfo.InvariantCulture)))));
        return (null, false, false);
    }

    private string? CheckMove(TurnRequest turn, ParsedAnswer answer, IGame game, List<FieldViolation> violations, bool last)
    {
        if (!answer.HasJson)
        {
            return null;
        }

        var text = answer.Move;
        if (turn.Move == FieldRule.Forbidden || text is null)
        {
            AddPresenceViolation("move", turn.Move, text, violations);
            return null;
        }

        var check = game.Check(turn, text);
        if (check.IsValid)
        {
            return check.NormalizedMove;
        }

        if (!(last && turn.Move == FieldRule.Optional))
        {
            violations.Add(new FieldViolation("move", prompts.RenderGame(check.Reason!), check.Allowed ?? AllowedFrom(turn)));
        }

        return null;
    }

    private static string AllowedFrom(TurnRequest turn) =>
        turn.Format?.AllowedValues is { Count: > 0 } values ? string.Join(", ", values.Select(v => $"\"{v}\"")) : string.Empty;

    private void AddPresenceViolation(string key, FieldRule rule, string? text, List<FieldViolation> violations)
    {
        if (rule == FieldRule.Required && text is null)
        {
            violations.Add(new FieldViolation(key, prompts.Arena("violation.missing")));
        }
        else if (rule == FieldRule.Forbidden && text is not null)
        {
            violations.Add(new FieldViolation(key, prompts.Arena("violation.forbidden")));
        }
    }

    private static (string Text, bool Truncated) Shorten(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? (text, false) : (string.Join(' ', words.Take(maxWords)), true);
    }

    private static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
