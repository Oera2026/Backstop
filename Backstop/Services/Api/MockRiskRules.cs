using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>
/// MOCK: phrase rules standing in for the real scam model. They find warning signs, mark where they are
/// in the text, and turn them into a verdict. A real back end replaces all of this.
/// </summary>
public static partial class MockRiskRules
{
    private sealed record Rule(RiskFlag Flag, Regex Pattern, int Weight, string Because);

    private static readonly Rule[] Rules =
    [
        new(new("money", "gift", "Asks for money or gift cards", "People you know rarely ask for money or gift cards by message."),
            Money(), 3, "it asks for money or gift cards"),
        new(new("private", "user", "Asks for private details", "Bank details, PINs and passwords are best kept to yourself."),
            Private(), 3, "it asks for private details"),
        new(new("secret", "eye-off", "Asks you to keep this secret", "Being asked not to tell your family is a warning sign."),
            Secret(), 2, "it asks you to keep it a secret"),
        new(new("new-number", "phone", "Says they have a new number", "People pretending to be family often say they have a new phone."),
            NewNumber(), 2, "it says it is from a new number"),
        new(new("other-app", "chat", "Asks you to move to another app", "Moving the chat somewhere else means nobody else can see it."),
            OtherApp(), 2, "it asks you to move to another app"),
        new(new("hurry", "clock", "Pushes you to act fast", "Being rushed makes it hard to think. It is fine to wait."),
            Hurry(), 1, "it pushes you to act fast"),
        new(new("feelings", "heart", "Very loving, very quickly", "Strong feelings from someone you have not met can be a trick."),
            Feelings(), 1, "it is very loving very quickly"),
        new(new("link", "link", "Has a link in it", "Links can lead to fake websites. We have taken the link out."),
            Link(), 1, "it has a link in it"),
    ];

    public static IReadOnlyList<RiskFlag> AllFlags { get; } = Rules.Select(r => r.Flag).ToList();

    public sealed record Finding(IReadOnlyList<RiskFlag> Flags, IReadOnlyList<Highlight> Highlights, int Score, string? TopBecause);

    /// <summary>Finds warning signs in one piece of text.</summary>
    public static Finding Scan(string text)
    {
        var hits = new List<Highlight>();
        var found = new List<Rule>();
        foreach (var rule in Rules)
        {
            var matches = rule.Pattern.Matches(text);
            if (matches.Count == 0) continue;
            found.Add(rule);
            hits.AddRange(matches.Select(m => new Highlight(m.Index, m.Length, rule.Flag.Id)));
        }
        var ordered = found.OrderByDescending(r => r.Weight).ToList();
        return new Finding(
            ordered.Select(r => r.Flag).ToList(),
            WithoutOverlaps(hits),
            found.Sum(r => r.Weight),
            ordered.FirstOrDefault()?.Because);
    }

    public static Verdict VerdictFor(int score) => score switch
    {
        0 => Verdict.LooksSafe,
        <= 2 => Verdict.BeCareful,
        _ => Verdict.DontReply,
    };

    public static string ReasonFor(Verdict verdict, string? because, bool startedFriendly = false)
    {
        var why = startedFriendly && because is not null ? $"the chat started friendly, but now {because}" : because;
        return verdict switch
        {
            Verdict.LooksSafe => "We did not find anything worrying in this.",
            Verdict.BeCareful => $"Take your time before you reply, because {why}.",
            _ => $"Please don't reply yet, because {why}.",
        };
    }

    public static int Weight(string flagId) => Rules.First(r => r.Flag.Id == flagId).Weight;

    /// <summary>The "because …" phrase for a flag, used in the one-sentence reason.</summary>
    public static string? BecauseFor(string? flagId) => Rules.FirstOrDefault(r => r.Flag.Id == flagId)?.Because;

    // Keeps the first of any overlapping highlights so the text can be marked up cleanly.
    private static List<Highlight> WithoutOverlaps(List<Highlight> hits)
    {
        var kept = new List<Highlight>();
        var end = -1;
        foreach (var h in hits.OrderBy(h => h.Start).ThenByDescending(h => h.Length))
        {
            if (h.Start < end) continue;
            kept.Add(h);
            end = h.Start + h.Length;
        }
        return kept;
    }

    [GeneratedRegex(@"\bgift ?cards?\b|\bvouchers?\b|\bsend (?:me )?(?:the )?(?:money|codes?)\b|\bthe codes?\b|£\s?\d[\d,]*|\bbank transfer\b|\bpay (?:you|me) back\b|\blend me\b", RegexOptions.IgnoreCase)]
    private static partial Regex Money();
    [GeneratedRegex(@"\b(?:bank|card) details\b|\bpin\b(?= ?(?:number|code|is|:))|\bpassword\b|\bsort code\b|\baccount number\b|\bsecurity code\b|\bone[- ]time code\b", RegexOptions.IgnoreCase)]
    private static partial Regex Private();
    [GeneratedRegex(@"\bkeep (?:this|it) (?:between us|secret|to yourself|quiet)\b|\bdon'?t tell\b|\bdo not tell\b|\bbetween you and me\b|\bour (?:little )?secret\b", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();
    [GeneratedRegex(@"\b(?:my|this is my) new (?:number|phone)\b|\bnew number\b|\bdropped my phone\b|\blost my phone\b", RegexOptions.IgnoreCase)]
    private static partial Regex NewNumber();
    [GeneratedRegex(@"\b(?:the other|another|a different) app\b|\b(?:chat|talk|message me) (?:on|in) (?:the )?\w+ (?:instead|app)\b|\bmore private\b|\bwhatsapp\b|\btelegram\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherApp();
    [GeneratedRegex(@"\burgent(?:ly)?\b|\bright now\b|\bimmediately\b|\btoday\b|\bas soon as possible\b|\basap\b|\bhurry\b|\bstraight away\b|\bwithin \d+ hours\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hurry();
    [GeneratedRegex(@"\bmy darling\b|\bi love you\b|\bsoul ?mate\b|\bvery special to me\b|\bknown you for years\b|\bmeant to be\b", RegexOptions.IgnoreCase)]
    private static partial Regex Feelings();
    [GeneratedRegex(@"\[link removed\]|\bhttps?://\S+|\bwww\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Link();
}
