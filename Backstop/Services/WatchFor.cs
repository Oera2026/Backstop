using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>
/// The "Next time, watch for…" teaching tip shown on blocked and held results. One short sentence about one thing to spot.
/// The tip comes from the checker's <c>watchFor</c> field when that is clean, and from this fixed library when it is not.
/// The email is untrusted data: a model tip that holds an order, an address, a link or a number is thrown away, never shown.
/// </summary>
public static partial class WatchFor
{
    public const int MaxWords = 20;
    public const int MaxChars = 160;

    /// <summary>The fixed tips, by the kind of signal. Written for reading age 9 to 11, one sentence each, no blame.</summary>
    public static IReadOnlyDictionary<string, string> Tips { get; } = new Dictionary<string, string>
    {
        ["money"] = "Real friends and banks never ask you to send gift cards or money in a hurry.",
        ["private"] = "Real companies never ask for your PIN or your full password in a message.",
        ["secret"] = "If someone asks you to keep a secret from your family, stop and tell someone you trust.",
        ["new-number"] = "If someone says they have a new number, call their old number first to check.",
        ["other-app"] = "Real banks and shops never ask you to move the chat to WhatsApp or another app.",
        ["remote"] = "Real helpers never ask to take control of your computer or to install a program.",
        ["hurry"] = "Scammers hurry you, but a real message can always wait while you ask someone.",
        ["feelings"] = "Take care when someone you have never met says very loving things very quickly.",
        ["link-hidden"] = "A link that hides where it goes is a warning, so ask your helper before opening it.",
        ["link"] = "Check where a link really goes before you open it, and ask your helper if you are unsure.",
        ["sender"] = "Check the sender's address, not just the name, because look-alike names are easy to fake.",
        ["injection"] = "A real message never tries to tell a checker what to say, so that is a sign of a trick.",
        ["at-risk"] = "If something feels wrong or frightening, tell someone you trust, because you are never a bother.",
        ["general"] = "If a message feels odd, wait and ask someone you trust before you reply.",
    };

    // The most telling signal wins. Signal ids come from the rules, the link check, the sender check and the checker's flagIds.
    private static readonly string[] Priority =
        ["injection", "remote", "link-hidden", "money", "private", "new-number", "other-app", "sender", "secret", "link", "hurry", "feelings", "at-risk"];

    /// <summary>The fixed tip for these signals. Never empty: with no known signal it is the general tip.</summary>
    public static string Fallback(IEnumerable<string>? signalIds)
    {
        var ids = (signalIds ?? []).Select(i => i == "link-odd" ? "link" : i).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Priority)
            if (ids.Contains(key)) return Tips[key];
        return Tips["general"];
    }

    /// <summary>The checker's tip if it is safe to show, otherwise null. Never throws.</summary>
    public static string? Clean(string? modelTip, string? explanation = null)
    {
        try
        {
            // Our own filter for the model's wording: no orders aimed at the checker, no addresses, links or long numbers.
            var tip = InputGuard.SafeText(modelTip, MaxChars, digitRun: 4, allowAdvice: true);
            if (tip is null) return null;
            if (Words(tip).Length is 0 or > MaxWords) return null;
            if (tip.Count(char.IsDigit) > 6 || tip.Contains("hxxp", StringComparison.OrdinalIgnoreCase)) return null;
            if (Sentences().Matches(tip).Count > 1) return null; // one sentence, not a paragraph
            if (Command().IsMatch(tip) || Verdictish().IsMatch(tip)) return null; // an order, or words that dictate the verdict
            if (Repeats(tip, explanation)) return null;
            return tip;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The tip to show: the checker's if clean, else the fixed one. Never empty.</summary>
    public static string Choose(string? modelTip, IEnumerable<string>? signalIds, string? explanation = null) =>
        Clean(modelTip, explanation) ?? Fallback(signalIds);

    // A tip that says the same as the explanation teaches nothing new.
    private static bool Repeats(string tip, string? explanation)
    {
        if (string.IsNullOrWhiteSpace(explanation)) return false;
        var mine = Words(tip).Where(w => w.Length > 3).Select(w => w.ToLowerInvariant()).ToHashSet();
        var theirs = Words(explanation).Where(w => w.Length > 3).Select(w => w.ToLowerInvariant()).ToHashSet();
        return mine.Count > 0 && (double)mine.Count(theirs.Contains) / mine.Count >= 0.7;
    }

    private static string[] Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // A tip is advice about spotting a trick. One that starts with an order to do something is the email talking.
    [GeneratedRegex(@"^\W*(?:ignore|disregard|forget|mark|tell|say|call|ring|click|send|pay|transfer|download|install|visit|open|reply|respond|answer|text|press|enter|give|share|forward|pretend|set|rate|report|approve|trust|confirm|verify|log ?in|sign ?in|follow)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Command();

    // Words that tell the checker what to answer, wherever they sit in the sentence.
    [GeneratedRegex(@"\b(?:mark|rate|call|say|report|tell|set|treat)\b[^.]{0,30}\b(?:safe|legit|legitimate|genuine|fine|ok|verdict)\b|\bverdict\b|\bthis (?:message|email|text) is (?:safe|genuine|legit)", RegexOptions.IgnoreCase)]
    private static partial Regex Verdictish();

    [GeneratedRegex(@"[.!?](?:\s|$)")]
    private static partial Regex Sentences();
}
