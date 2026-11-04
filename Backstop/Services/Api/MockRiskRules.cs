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
        // A price on its own (a raffle ticket, a bill) is only worth a note. Asking for an amount is Money above.
        new(new("amount", "gift", "Mentions an amount of money", "Check the amount is what you expect before you act on it."),
            Amount(), 1, "it mentions an amount of money"),
        new(new("private", "user", "Asks for private details", "Bank details, PINs and passwords are best kept to yourself."),
            SensitivePatterns.Private, 3, "it asks for private details"),
        new(new("secret", "eye-off", "Asks you to keep this secret", "Being asked not to tell your family is a warning sign."),
            Secret(), 2, "it asks you to keep it a secret"),
        new(new("new-number", "phone", "Says they have a new number", "People pretending to be family often say they have a new phone."),
            NewNumber(), 2, "it says it is from a new number"),
        new(new("other-app", "chat", "Asks you to move to another app", "Moving the chat somewhere else means nobody else can see it."),
            OtherApp(), 2, "it asks you to move to another app"),
        new(new("remote", "monitor", "Asks to control the computer", "Someone asking to take control of your computer, or to install software so they can, is a common scam."),
            Remote(), 3, "it asks to control your computer"),
        new(new("hurry", "clock", "Pushes you to act fast", "Being rushed makes it hard to think. It is fine to wait."),
            Hurry(), 1, "it pushes you to act fast"),
        new(new("feelings", "heart", "Very loving, very quickly", "Strong feelings from someone you have not met can be a trick."),
            Feelings(), 1, "it is very loving very quickly"),
        new(new("link", "link", "Has a link in it", "Links can lead to fake websites. We have taken the link out."),
            LinkFinder.PatternOrRemoved, 1, "it has a link in it"),
        // Someone may be in danger or distress. Always goes to a human, whatever else the checks say.
        new(new("at-risk", "heart", "Your helper is being told", "Someone you trust will look at this and may check in with you."),
            AtRisk(), 2, "it sounds like someone may need help"),
        // Orders aimed at the checker. Matched by InputGuard, not by a pattern here.
        new(new("injection", "alert", "Tries to tell our checker what to say", "A real message does not do this. Your helper will look at it."),
            Never(), 2, "it tries to tell our checker what to say"),
    ];

    public static IReadOnlyList<RiskFlag> AllFlags { get; } = Rules.Select(r => r.Flag).ToList();

    public sealed record Finding(IReadOnlyList<RiskFlag> Flags, IReadOnlyList<Highlight> Highlights, int Score, string? TopBecause);

    /// <summary>Finds warning signs in one piece of text.</summary>
    public static Finding Scan(string text)
    {
        var hits = new List<Highlight>();
        var found = new List<Rule>();
        // Rules read a folded copy too, so zero-width letters, full-width letters and look-alikes cannot hide a phrase.
        // Highlights still point into the text as written.
        var folded = InputGuard.Fold(text);
        var injected = InputGuard.LooksLikeInjection(text);
        foreach (var rule in Rules)
        {
            if (rule.Flag.Id == "injection")
            {
                if (injected) found.Add(rule);
                continue;
            }
            try
            {
                var matches = rule.Pattern.Matches(text);
                if (matches.Count == 0 && !rule.Pattern.IsMatch(folded)) continue;
                hits.AddRange(matches.Select(m => new Highlight(m.Index, m.Length, rule.Flag.Id)));
            }
            catch (RegexMatchTimeoutException) { /* could not finish reading it, so the warning stands: fail closed */ }
            found.Add(rule);
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

    [GeneratedRegex(@"\bgift ?cards?\b|\bvouchers?\b|\bsend (?:me )?(?:the )?(?:money|codes?)\b|\b(?:read|tell|give|text) (?:me |us )?the (?:\d+[- ]?digit )?codes?\b|\bcodes? on the back\b|\b(?:send|transfer|pay|need|lend|owe)\b[^.\n]{0,30}£\s?\d|\b(?:bank|wire) transfers?\b|\bwire (?:me|the|it|money)\b|\bpay (?:you|me) back\b|\blend me\b|\bgift-cards?\b|\b(?:itunes|apple|steam|google play|amazon|ebay) (?:gift )?(?:cards?|vouchers?)\b|\bprepaid cards?\b|\bcrypto(?:currency|currencies)?\b|\bbitcoin\b|\bethereum\b|\bwestern union\b|\bmoneygram\b|\bsafe account\b|\b(?:send|transfer|pay|need|lend|owe)\b[^.\n]{0,30}(?:[$€]\s?\d|\b\d[\d,]*(?:\.\d{1,2})?\s?(?:pounds?|quid|dollars?|euros?)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Money();
    [GeneratedRegex(@"[£$€]\s?\d[\d,]*|\b\d[\d,]*(?:\.\d{1,2})?\s?(?:pounds?|quid|dollars?|euros?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Amount();
    [GeneratedRegex(@"\bkeep (?:this|it) (?:between us|secret|to yourself|quiet)\b|\bdon'?t tell\b|\bdo not tell\b|\bbetween you and me\b|\bour (?:little )?secret\b", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();
    [GeneratedRegex(@"\b(?:my|this is my) new (?:number|phone)\b|\bnew number\b|\bdropped my phone\b|\blost my phone\b", RegexOptions.IgnoreCase)]
    private static partial Regex NewNumber();
    [GeneratedRegex(@"\b(?:the other|another|a different) app\b|\b(?:chat|talk|message me) (?:on|in) (?:the )?\w+ (?:instead|app)\b|\bmore private\b|\bwhatsapp\b|\btelegram\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherApp();
    [GeneratedRegex(@"\banydesk\b|\bteamviewer\b|\bultraviewer\b|\bremote (?:access|control|support|desktop)\b|\byour computer has (?:a )?(?:virus|been (?:hacked|infected))\b|\bread (?:us|me) (?:the|your) \d* ?digit code\b", RegexOptions.IgnoreCase)]
    private static partial Regex Remote();
    // "today" and "immediately" are everyday words ("Dinner today?"), so they only count when they press for an action.
    [GeneratedRegex(@"\burgent(?:ly)?\b|\bright now\b|\bact now\b|\blast chance\b|\bfinal (?:notice|warning|reminder)\b|\b(?:account|card|access|service|subscription|membership|licen[cs]e)\b[^.\n]{0,30}\b(?:suspended|frozen|terminated|locked)\b|\b(?:will be|has been|have been|was) suspended\b|\bsuspended (?:your|the|my) \w+\b|\bas soon as possible\b|\basap\b|\bhurry\b|\bstraight away\b|\bwithin \d+ hours\b|\b(?:by|before) (?:the end of )?today\b|\b(?:today|immediately),? or\b|\b(?:book|reply|respond|pay|call|act|send|click|confirm|buy)\b[^.\n]{0,40}\b(?:today|immediately)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hurry();
    [GeneratedRegex(@"\bmy darling\b|\bi love you\b|\bsoul ?mate\b|\bvery special to me\b|\bknown you for years\b|\bmeant to be\b", RegexOptions.IgnoreCase)]
    private static partial Regex Feelings();
    // Words about someone's own safety. Ordinary uses stay out: "can't go on Friday", "my son beats me at chess", "he hits me up for a lift".
    [GeneratedRegex(@"\b(?:want|wants|wanting) to die\b|\bi wish i (?:was|were) dead\b|\bend(?:ing)? it all\b|\btake my own life\b|\bnot worth living\b|\bno reason to live\b|\bbetter off without me\b|\bcan'?t (?:go|carry) on(?: any ?more| like this)?\s*(?:[.!,]|$)|" +
        @"\bmy (?:son|daughter|husband|wife|partner|carer|care worker|grandson|granddaughter|nephew|niece|brother|sister|neighbou?r|boyfriend|girlfriend|lodger) (?:\w+ )?(?:hits?|hurts?|beats?|threatens?|threatened|slaps?|slapped|kicks?|kicked|punch(?:es|ed)?|pushe[sd]|grabs|grabbed) me\b(?! (?:up|at|in|to|every|with a)\b)|" +
        @"\b(?:he|she|they) (?:hit|beat|slapped|kicked|punched|pushed|threatened) me\b(?! (?:up|at|in|to|every|with a)\b)|" +
        @"\b(?:he|she|they|my (?:son|daughter|husband|wife|partner|carer|care worker|grandson|granddaughter|nephew|niece|brother|sister|neighbou?r|boyfriend|girlfriend|lodger)) (?:(?:always|keeps?) )?(?:takes?|took|has taken|keeps?|stole|steals?|has stolen|controls?|spends?|spent|emptied) (?:all )?my (?:pension|money|savings|benefits|bank card|pin)\b|" +
        @"\b(?:forc\w+|pressur\w+|mak(?:es|ing)|made) me (?:to )?(?:sign|hand over|transfer|withdraw|change my will|give (?:him|her|them) (?:all )?(?:my |the )?(?:money|pension|savings|pin|card))\b|" +
        @"\bi(?:'?m| am) not allowed (?:to (?:see|talk|speak|call|ring|leave|go out|use the phone)|out)\b|\b(?:he|she|they|my \w+) (?:won'?t|will not|doesn'?t|does not|don'?t) let me (?:see|talk|speak|call|ring|leave|go out)\b|\b(?:scared|afraid|frightened) (?:to go home|of my (?:son|daughter|husband|wife|partner|carer|care worker|grandson|granddaughter|nephew|niece|brother|sister|neighbou?r|boyfriend|girlfriend|lodger))\b|\b(?:threatened|threatens|threatening) to (?:put me|send me|hurt me|leave me|kick me)\b|" +
        @"\bkill(?:ing)? (?:myself|yourself|you|her|him|them)\b|\bend (?:my|your) life\b|\bsuicid\w*|\bself[- ]?harm\b|\bhurt(?:ing)? (?:myself|yourself|you)\b|\bdon'?t want to (?:be here|live|go on)\b|\bno one would miss me\b|\b(?:scared|afraid|frightened) of (?:him|her|them)\b|\b(?:he|she|they) (?:hits?|hurts?|threatens?|beats?) me\b|\bi(?:'?m| am) (?:in danger|being (?:threatened|blackmailed|abused|held|followed))\b|\bi know where you live\b|\bhelp me,? please\b|\bnot safe (?:here|at home)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AtRisk();
    [GeneratedRegex(@"(?!)")]
    private static partial Regex Never();
}
