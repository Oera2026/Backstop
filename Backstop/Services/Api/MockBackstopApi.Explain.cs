using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services.Api;

// Explain: the AI reads and breaks down the document (see MetaDocumentExplainer). Here the local checks set a floor
// the AI cannot lower, and the AI's hard words are found in the text so they can be marked.
public sealed partial class MockBackstopApi
{
    private static Explanation BuildExplanation(DocumentReading reading, string original, ReadingLevel level, ExplainFormat format)
    {
        // Floor: warning signs found by the local rules count even if the AI saw none. The AI can only add to this.
        var risky = reading.Suspicious || MockRiskRules.VerdictFor(MockRiskRules.Scan(original).Score) != Verdict.LooksSafe;

        var chunks = new List<ExplainChunk>(reading.Chunks);
        var overview = reading.Overview;
        if (risky)
        {
            chunks.Insert(0, new("Before you do anything", "This has warning signs. Tap \"Is this safe?\" to check it first."));
            if (!overview.Contains("warning sign", StringComparison.OrdinalIgnoreCase))
                overview = "This has some warning signs. Check it is safe before you do anything. " + overview;
        }

        return new Explanation(NewId("x"), reading.Title, overview, reading.Deadline, chunks, reading.Actions,
            original, FindHardWords(original, reading.HardWords), level, format);
    }

    private static string Shorten(string s) => s.Length <= 90 ? s : s[..87].TrimEnd() + "…";

    /// <summary>The first time each of the AI's hard words appears in the text, longest first, never overlapping.</summary>
    private static List<HardWord> FindHardWords(string text, IReadOnlyList<(string Word, string Meaning)> words)
    {
        var found = new List<HardWord>();
        foreach (var (word, meaning) in words.OrderByDescending(w => w.Word.Length))
        {
            var m = Regex.Matches(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)
                .FirstOrDefault(m => !found.Any(f => m.Index < f.Start + f.Length && f.Start < m.Index + m.Length));
            if (m is not null) found.Add(new HardWord(m.Index, m.Length, meaning));
        }
        return found.OrderBy(f => f.Start).ToList();
    }
}
