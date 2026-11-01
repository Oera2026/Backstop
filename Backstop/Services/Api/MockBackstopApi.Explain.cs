using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

// MOCK: how Explain "rewrites" text. The council letter has hand-written wording at each reading level;
// anything else is cut into small parts with simple rules. A real back end replaces all of this.
public sealed partial class MockBackstopApi
{
    private static Explanation ExplainCouncilLetter(string text, ReadingLevel level, ExplainFormat format) => new(
        NewId("x"), ExplainFixtures.Title, ExplainFixtures.OverviewAt(level), ExplainFixtures.Deadline,
        ExplainFixtures.ChunksFor(level, format), ExplainFixtures.Actions, text, FindHardWords(text), level, format);

    private static Explanation ExplainAnything(string text, ReadingLevel level, ExplainFormat format)
    {
        // Lines first (a chat has one message per line), then sentences.
        var sentences = text.Split('\n').SelectMany(line => Sentence().Split(line)).Select(s => s.Trim()).Where(s => s.Length > 1).ToList();
        var deadline = DeadlinePattern().Match(text) is { Success: true } d
            ? $"{char.ToUpperInvariant(d.Groups[1].Value[0])}{d.Groups[1].Value[1..].ToLowerInvariant()} {d.Groups[2].Value}" : null;
        // Any warning sign at all (not only the strongest) means "check it first", the same test Ask uses.
        var risky = MockRiskRules.VerdictFor(MockRiskRules.Scan(text).Score) != Verdict.LooksSafe;
        var actions = risky
            ? ["Check it is safe before you do anything"]
            : sentences.Where(s => ActionPattern().IsMatch(s)).Select(Shorten).Take(4).ToList();

        var overview = risky
            ? "This message has some warning signs. Check it is safe before you do anything."
            : actions.Count > 0
                ? $"This is a message for you. It asks you to: {LowerFirst(actions[0]).TrimEnd('.')}."
                : "This is a message for you. You do not need to do anything.";

        var perChunk = level switch { ReadingLevel.VerySimple => 1, ReadingLevel.Simple => 2, _ => 3 };
        var chunks = new List<ExplainChunk>();
        if (risky) chunks.Add(new("Before you do anything", "This message has warning signs. Tap \"Is this safe?\" to check it first."));

        switch (format)
        {
            case ExplainFormat.Steps when actions.Count > 0:
                chunks.AddRange(actions.Select((a, i) => new ExplainChunk($"Step {i + 1}", a)));
                break;
            case ExplainFormat.Questions:
                chunks.Add(new("What is this?", sentences.FirstOrDefault() ?? "A short message."));
                chunks.Add(new("Do I need to do anything?", actions.Count == 0 ? "No." : "Yes. " + string.Join(" ", actions)));
                chunks.Add(new("Is there a date?", deadline is null ? "No date is given." : $"{deadline}."));
                chunks.Add(new("Is it safe?", risky ? "It has warning signs. Tap \"Is this safe?\" to check it." : "We did not find warning signs. You can still tap \"Is this safe?\" to check."));
                break;
            default:
                for (var i = 0; i < sentences.Count; i += perChunk)
                {
                    var part = string.Join(" ", sentences.Skip(i).Take(perChunk));
                    chunks.Add(new(HeadingFor(part, i / perChunk + 1), part));
                }
                break;
        }

        var firstLine = text.Split('\n')[0].Trim();
        var title = firstLine.Length is > 0 and <= 40 ? firstLine.TrimEnd(',', '.') : "Your message";
        return new Explanation(NewId("x"), title, overview, deadline, chunks, actions, text, FindHardWords(text), level, format);
    }

    private static string HeadingFor(string part, int n) =>
        Speaker().Match(part) is { Success: true } who ? $"{who.Groups[1].Value} says"
        : MoneyWords().IsMatch(part) ? "About money"
        : DeadlinePattern().IsMatch(part) ? "About dates"
        : ActionPattern().IsMatch(part) ? "What it asks"
        : n == 1 ? "How it starts" : $"Part {n}";

    private static string Shorten(string s) => s.Length <= 90 ? s : s[..87].TrimEnd() + "…";

    /// <summary>The first time each glossary word appears, longest words first, never overlapping.</summary>
    private static List<HardWord> FindHardWords(string text)
    {
        var found = new List<HardWord>();
        foreach (var (word, meaning) in Glossary.Words.OrderByDescending(w => w.Key.Length))
        {
            var m = Regex.Matches(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)
                .FirstOrDefault(m => !found.Any(f => m.Index < f.Start + f.Length && f.Start < m.Index + m.Length));
            if (m is not null) found.Add(new HardWord(m.Index, m.Length, meaning));
        }
        return found.OrderBy(f => f.Start).ToList();
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex Sentence();
    // "by 14 November", "before Friday", "until 5pm", "by tomorrow", "by 5 o'clock".
    [GeneratedRegex(@"\b(by|before|until|no later than) ((?:\d{1,2}(?:st|nd|rd|th)? (?:January|February|March|April|May|June|July|August|September|October|November|December)(?: \d{4})?)|(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)|today|tomorrow|tonight|(?:\d{1,2}(?:[:.]\d{2})? ?(?:am|pm))|(?:\d{1,2} o'clock))(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex DeadlinePattern();
    [GeneratedRegex(@"\b(please|you need to|you must|can you|could you|send|call|reply|pay|buy|fill in|book|return)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ActionPattern();
    [GeneratedRegex(@"£|\bmoney\b|\bpay\b|\bbill\b|\bgift cards?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyWords();
    [GeneratedRegex(@"^([\p{L}][\p{L} .'-]{0,30}?):\s")]
    private static partial Regex Speaker();
}
