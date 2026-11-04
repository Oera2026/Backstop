using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>
/// Defences for text that someone else wrote. Used before the local rules read it, before it is sent to the
/// model, and on anything the model sends back. Nothing here trusts the content.
/// </summary>
public static partial class InputGuard
{
    /// <summary>Most characters the checker will read. Longer text is cut, and a cut message can never be called safe.</summary>
    public const int MaxCheckedChars = 8000;

    /// <summary>Most characters of the model's own wording we will ever show.</summary>
    public const int MaxReasonChars = 220;

    /// <summary>
    /// Folds text so look-alike tricks cannot hide a phrase from the rules: zero-width and direction-control
    /// characters are removed, full-width and styled letters become plain ones, spaces are tidied.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string normal;
        try { normal = text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { normal = text; } // broken surrogates: keep going on the raw text

        var sb = new StringBuilder(normal.Length);
        foreach (var ch in normal)
        {
            var cat = char.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.Format || cat == UnicodeCategory.PrivateUse || cat == UnicodeCategory.OtherNotAssigned) continue;
            if (cat == UnicodeCategory.Control && ch is not ('\n' or '\r' or '\t')) continue;
            sb.Append(Confusable(ch));
        }
        return sb.ToString();
    }

    // The few Cyrillic and Greek letters that look exactly like Latin ones and are used to dodge word lists.
    private static char Confusable(char c) => c switch
    {
        'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c', 'х' => 'x', 'у' => 'y', 'і' => 'i', 'ѕ' => 's',
        'А' => 'A', 'В' => 'B', 'Е' => 'E', 'К' => 'K', 'М' => 'M', 'Н' => 'H', 'О' => 'O', 'Р' => 'P', 'С' => 'C', 'Т' => 'T', 'Х' => 'X',
        'ο' => 'o', 'α' => 'a', 'ε' => 'e', 'ι' => 'i', 'ν' => 'v', 'Ο' => 'O', 'Α' => 'A', 'Ε' => 'E', 'Ι' => 'I', 'Ν' => 'N',
        _ => c,
    };

    /// <summary>True if the text was longer than the checker reads.</summary>
    public static bool IsTooLong(string? text) => (text?.Length ?? 0) > MaxCheckedChars;

    /// <summary>Folds, then cuts to the length the checker reads.</summary>
    public static string ForChecker(string? text)
    {
        var folded = Fold(text);
        return folded.Length > MaxCheckedChars ? folded[..MaxCheckedChars] : folded;
    }

    /// <summary>
    /// True if the text tries to give orders to the checker or to whatever reads it next. A real message
    /// from a real person has no reason to do this, so it counts as a warning sign in its own right.
    /// </summary>
    public static bool LooksLikeInjection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = Fold(text);
        return InjectionPhrases().IsMatch(t) || Squashed().IsMatch(Squash(t));
    }

    // "i g n o r e", "ig-nore", "ignore_previous" -> letters only, so spaced-out words are still caught.
    private static string Squash(string t) => NonLetters().Replace(t.ToLowerInvariant(), "");

    /// <summary>
    /// Makes the model's own words safe to show: one short plain sentence. Returns null when the wording
    /// is not fit to show, and the caller uses fixed wording for the verdict instead.
    /// </summary>
    public static string? SafeReason(string? reason) => SafeText(reason, MaxReasonChars);

    /// <summary>Same checks for a longer reply. Runs of <paramref name="digitRun"/> or more digits are refused (phone and account numbers).</summary>
    public static string? SafeText(string? text, int max, int digitRun = 4, bool allowAdvice = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var r = Fold(text).Replace('\r', ' ').Replace('\n', ' ').Trim();
        r = Spaces().Replace(r, " ");
        if (r.Length == 0 || r.Length > max) return null;
        if (LooksLikeInjection(r)) return null;
        if (Unsafe().IsMatch(r) || HasLongNumber(r, digitRun)) return null;
        if (!allowAdvice && Orders().IsMatch(r)) return null;
        return r;
    }

    // A run of digits is refused (phone and account numbers), except a plain year such as 2026.
    private static bool HasLongNumber(string r, int digitRun) =>
        Regex.Matches(r, $@"\d{{{digitRun},}}").Any(m => !(m.Length == 4 && (m.Value.StartsWith("19") || m.Value.StartsWith("20"))));

    /// <summary>A mail address and nothing else. Rejects line breaks, spaces, several addresses and odd characters.</summary>
    public static bool IsPlainAddress(string? a) =>
        !string.IsNullOrWhiteSpace(a) && a.Length <= 254 && Address().IsMatch(a);

    /// <summary>One line of text (a subject): no line breaks or control characters, and not too long.</summary>
    public static bool IsSingleLine(string? s, int max) =>
        s is not null && s.Length <= max && !s.Any(char.IsControl);

    // Every order has to name what it is aimed at (instructions, rules, the assistant, the model), because the bare words are
    // everyday English: "don't forget your coat", "you are now a great-grandma", "please ignore the previous email".
    [GeneratedRegex(
        @"ignore (?:all |any |the |your |every )?(?:of )?(?:the |your )?(?:previous|prior|above|earlier|preceding|former) (?:instructions?|rules|prompts?|directions|commands|guidelines)" +
        @"|ignore (?:all |any |every |of )*(?:your|these|my) (?:own )?(?:instructions|rules|prompts?|guidelines|training)" +
        @"|disregard (?:all |any |the |your )?(?:of )?(?:the |your )?(?:previous |prior |above |earlier )?(?:instructions|rules|prompts?|guidelines)" +
        @"|forget (?:all |about )?(?:your |the |all |previous |prior |of )*(?:instructions|rules|prompts?|guidelines|training)" +
        @"|override (?:the |your |all )?(?:system|rules|instructions|verdict|safety)" +
        @"|you are now (?:an? |the )?(?:ai |new |different |unrestricted |unfiltered |jailbroken )?(?:assistant|ai|model|chatbot|bot|checker|system)\b|you are now in \w+ mode" +
        @"|from now on,? you (?:will|must|shall|should|are to) (?:answer|reply|respond|say|output|only|always|ignore|act|pretend)" +
        @"|act as (?:a |an |the )?(?:different|new|unrestricted|unfiltered) (?:ai|assistant|model|chatbot|bot|checker|system)\b" +
        @"|new (?:system )?instructions?\s*:|(?:here are|these are|follow) (?:your |the |my |these )?new instructions" +
        @"|system prompt|developer message|jailbreak|\bdan mode\b" +
        @"|pretend (?:you are|to be) (?:an? |the )?(?:different |new |unrestricted |evil )?(?:ai|assistant|model|chatbot|bot|checker|system)\b" +
        @"|(?:set|output|return|respond with|answers?|reply with|mark(?: this)? as|rate(?: this)? as|say)\W{0,4}(?:the )?(?:verdict|result|answer|rating)?\W{0,4}(?:is |as |to |with )?\W{0,3}looks ?safe" +
        @"|verdict\W{1,4}looks ?safe|this (?:message |email |text )?is (?:completely |totally |perfectly )?(?:safe|legitimate|genuine|not a scam)\b.{0,40}(?:tell|say|report|mark)" +
        @"|</?(?:system|assistant|user|instructions?)>|\[/?(?:inst|system)\]|<\|(?:im_start|im_end|system|endoftext)\|>|begin (?:inspected|untrusted)|end (?:inspected|untrusted)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionPhrases();

    // The same orders with the gaps taken out, for text spaced or punctuated to dodge the main pattern.
    // As above, each one names its target, so "you are nowhere near" and "new instructions for your tablets" pass.
    [GeneratedRegex(@"ignore(?:all|any|the|your|every)?(?:previous|prior|above|earlier)(?:instructions?|rules|prompts?)|disregard(?:all|any|the|your)?(?:previous|prior|above)?(?:instructions|rules|prompts?)|systemprompt|(?:verdict|output|return|answer)(?:is|as|to)?lookssafe|overridetheverdict", RegexOptions.CultureInvariant)]
    private static partial Regex Squashed();

    [GeneratedRegex(@"[^a-z]")]
    private static partial Regex NonLetters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // Anything in the model's wording that could send someone somewhere or ask them to do something outside Backstop.
    [GeneratedRegex(@"://|www\.|\.(?:com|net|org|co|uk|io|ru|xyz|ly)\b|@|<|>", RegexOptions.IgnoreCase)]
    private static partial Regex Unsafe();

    // Words that read as an order to do something. A scam warning ("do not click the link") may use them, so callers
    // that write advice can allow them; the checker's one-line reason may not.
    [GeneratedRegex(@"\bclick\b|\bdownload\b|\binstall\b|\bcall (?:this|the) number\b", RegexOptions.IgnoreCase)]
    private static partial Regex Orders();

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex Address();
}
