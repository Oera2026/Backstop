using System.Text.RegularExpressions;
using Backstop.Services.Api;

namespace Backstop.Services;

public enum AskRoute
{
    /// <summary>A conversation with the AI.</summary>
    Talk,
    /// <summary>The typed text holds the message to check.</summary>
    CheckText,
    /// <summary>"Is this a scam?" about the email that is open.</summary>
    CheckOpenEmail,
    CheckPicture,
    /// <summary>"What does this say?" with a picture.</summary>
    ExplainPicture,
    /// <summary>"Is this safe?" with nothing to check. Nothing is called safe.</summary>
    AskWhatToCheck,
}

/// <summary>Works out what a message in Ask Me is asking for. It never decides whether anything is safe.</summary>
public static partial class AskIntent
{
    private const int MostWordsLeftInAQuestion = 3;

    public static AskRoute Route(string text, bool hasPicture, bool hasOpenEmail)
    {
        var t = Normal(text);
        if (hasPicture) return ReadThis().IsMatch(t) ? AskRoute.ExplainPicture : AskRoute.CheckPicture;
        if (Reported().IsMatch(t) || !AsksToCheck().IsMatch(t)) return AskRoute.Talk;
        if (!NothingPasted(text, t)) return AskRoute.CheckText;
        return hasOpenEmail ? AskRoute.CheckOpenEmail : AskRoute.AskWhatToCheck;
    }

    /// <summary>The person says this happened to them ("I think I've been scammed"). That needs a person, not a verdict on the sentence.</summary>
    public static bool IsScamReport(string text) => Reported().IsMatch(Normal(text));

    // Curly apostrophes are the phone keyboard default.
    private static string Normal(string text) => text.Replace('’', '\'').Replace('‘', '\'').Trim().ToLowerInvariant();

    // Only the question is here: take away its usual words and almost nothing is left. Any warning sign or link means there is
    // something to check. A wrong guess only asks the person what to check, so it can never give false comfort.
    private static bool NothingPasted(string raw, string normal)
    {
        if (LinkFinder.Inspect(raw).Links.Count > 0 || MockRiskRules.Scan(raw).Score > 0) return false;
        var left = Words().Matches(normal).Count(m => !QuestionWords.Contains(m.Value));
        return left <= MostWordsLeftInAQuestion;
    }

    private static readonly HashSet<string> QuestionWords =
    [
        "is", "this", "that", "it", "a", "an", "the", "email", "mail", "message", "text", "letter", "one", "safe", "real", "genuine", "scam", "fake",
        "suspicious", "legit", "ok", "okay", "please", "can", "could", "you", "check", "tell", "me", "if", "do", "does", "think", "look", "looks", "like", "i", "from", "my",
    ];

    [GeneratedRegex(@"[\p{L}']+")]
    private static partial Regex Words();

    [GeneratedRegex(@"\bis (?:this|that|it)\b[^.?!\n]{0,20}\b(?:safe|real|genuine|legit|fake|ok|okay)\b|\bscam\b|\bsuspicious\b|\bis this (?:message|email)\b")]
    private static partial Regex AsksToCheck();

    [GeneratedRegex(@"\bscammed\b|\b(?:i|we|i've|ive|i have|we've)\b[^.?!\n]{0,30}\b(?:fell for|fallen for|been (?:conned|tricked|cheated|robbed)|lost (?:my|our|all my) (?:money|savings))\b")]
    private static partial Regex Reported();

    [GeneratedRegex(@"\bwhat (?:does|do) (?:this|it|that) (?:say|mean)\b|\bwhat is this\b|\bwhat's this\b|\bexplain\b|\bread (?:this|it|that)\b|\bhelp me (?:read|understand)\b")]
    private static partial Regex ReadThis();
}
