using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api;

namespace Backstop.Services;

/// <summary>
/// The local part of the checking pipeline: the Rules tier, the Link check tier and the Policy decision.
/// Everything here is worked out from the message itself. Nothing is scripted per message.
/// Links are judged by their address only. They are never opened, because the app may only talk to Meta and Supabase.
/// </summary>
public static partial class MailAnalyzer
{
    public sealed record Analysis(
        IReadOnlyList<Flag> Flags,
        IReadOnlyList<LinkCheck> Links,
        int RulesScore,
        string? TopBecause,
        bool AtRisk,
        bool DeceptiveLink,
        int LinkProblems,
        bool SenderProblem,
        TierResult RulesTier,
        TierResult LinkTier);

    public sealed record Decision(
        MessageState State,
        string Summary,
        Concern Concern,
        int RulesScore,
        IReadOnlyList<Flag> Flags,
        TierResult PolicyTier);

    /// <summary>Rules tier and Link check tier. Pure and fast.</summary>
    public static Analysis Analyze(InboundMail mail)
    {
        var text = $"{mail.Subject}\n{mail.Body}";

        // Rules: the same phrase rules Check uses. The link rule is left to the Link check tier.
        var scan = MockRiskRules.Scan(text);
        var found = scan.Flags.Where(f => f.Id != "link").ToList();
        var score = found.Sum(f => MockRiskRules.Weight(f.Id));
        var rulesTier = found.Count == 0
            ? new TierResult("Rules", TierStatus.Passed, "No warning words or phrases found.")
            : new TierResult("Rules", TierStatus.Flagged,
                string.Join(" ", found.Select(f => f.Title.TrimEnd('.') + ".")));

        // Link check: judged by the address only.
        var linkScan = LinkFinder.Inspect(text);
        var links = linkScan.Links;
        var problems = linkScan.Problems;
        var deceptive = linkScan.Deceptive;
        var linkTier = links.Count == 0
            ? new TierResult("Link check", TierStatus.Skipped, "No links.")
            : problems == 0
                ? new TierResult("Link check", TierStatus.Passed,
                    $"{links.Count} link{(links.Count == 1 ? "" : "s")} checked by address. No warning signs in the address.")
                : new TierResult("Link check", TierStatus.Flagged,
                    string.Join(" ", links.SelectMany(l => l.Signals).Distinct()));

        var flags = found.Select(f => new Flag(f.Icon, f.Title, f.Explanation)).ToList();
        if (deceptive)
            flags.Add(new Flag("link", "The link hides where it goes", "The address is disguised or shortened, so you cannot tell which website it leads to."));
        else if (problems > 0)
            flags.Add(new Flag("link", "The link looks unusual", "The address has words or a form that real senders rarely use."));

        // Sender: the name and the address must agree, and the address must not use look-alike letters.
        var senderProblem = SenderLooksWrong(mail.FromName, mail.FromAddress);
        if (senderProblem)
            flags.Add(new Flag("user", "The sender looks wrong", "The name shown does not match the address, or the address uses look-alike letters."));

        var top = found.OrderByDescending(f => MockRiskRules.Weight(f.Id)).Select(f => MockRiskRules.BecauseFor(f.Id)).FirstOrDefault();
        return new Analysis(flags, links, score, top, found.Any(f => f.Id == "at-risk"), deceptive, problems, senderProblem, rulesTier, linkTier);
    }

    [GeneratedRegex(@"[^\s@<>""']+@([^\s@<>""']+)|\b((?:[a-z0-9-]+\.)+[a-z]{2,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddressOrDomain();

    /// <summary>
    /// True when the sender name carries a different address or website than the real one, or the real address
    /// is written with look-alike (xn--) letters. Attachments are not part of InboundMail, so none are checked.
    /// </summary>
    private static bool SenderLooksWrong(string fromName, string fromAddress)
    {
        var at = fromAddress.LastIndexOf('@');
        var host = (at >= 0 ? fromAddress[(at + 1)..] : fromAddress).Trim().ToLowerInvariant();
        if (host.Length == 0 || host.Contains("xn--")) return true;

        foreach (Match m in AddressOrDomain().Matches(fromName ?? ""))
        {
            var shown = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant().TrimEnd('.');
            if (shown != host && !host.EndsWith("." + shown) && !shown.EndsWith("." + host)) return true;
        }
        return false;
    }

    /// <summary>
    /// Policy tier. Cleared only when every local check is clean AND Meta AI answered and agreed.
    /// Blocked only for a disguised link together with a strong warning, and never when someone may be at risk.
    /// Everything else, including a missing or failed AI answer, is Held.
    /// </summary>
    public static Decision Decide(Analysis a, MetaScamAnalysis? ai)
    {
        var aiAnswered = ai is { Success: true };
        var aiWorried = aiAnswered && (ai!.Verdict != Verdict.LooksSafe || ai.NeedsHuman || ai.AtRisk);
        var atRisk = a.AtRisk || (ai?.AtRisk ?? false);
        var localClean = a.RulesScore == 0 && a.LinkProblems == 0 && !a.SenderProblem;
        var strong = MockRiskRules.VerdictFor(a.RulesScore) == Verdict.DontReply;

        MessageState state;
        string note;
        if (a.DeceptiveLink && strong && !atRisk)
        { state = MessageState.Blocked; note = "Hidden link plus a strong warning sign. Blocked automatically."; }
        else if (localClean && aiAnswered && !aiWorried)
        { state = MessageState.Cleared; note = "Every check passed. Cleared."; }
        else if (!aiAnswered && localClean)
        { state = MessageState.Held; note = "The AI check did not give an answer, so a helper looks first."; }
        else
        { state = MessageState.Held; note = atRisk ? "Someone may need help, so a helper looks first." : "A check found a problem, so a helper looks first."; }

        var flags = new List<Flag>(a.Flags);
        if (aiWorried)
            foreach (var f in ai!.Flags)
                if (flags.All(x => x.Title != f.Title))
                    flags.Add(new Flag(f.Icon, f.Title, f.Explanation));

        string summary;
        if (state == MessageState.Cleared) summary = "Nothing unusual found.";
        else if (a.RulesScore > 0)
            summary = MockRiskRules.ReasonFor(MockRiskRules.VerdictFor(a.RulesScore), a.TopBecause);
        else if (aiWorried) summary = ai!.Reason;
        else if (a.LinkProblems > 0) summary = "A link in this message looks unusual.";
        else if (a.SenderProblem) summary = "The sender of this message looks wrong.";
        else summary = "The automatic check could not finish, so please look at this message with care.";

        // Worked out from the checks that ran. Cleared mail claims nothing about risk.
        var concern = state == MessageState.Cleared ? Concern.None
            : a.RulesScore >= 3 || a.DeceptiveLink || (aiWorried && ai!.Verdict == Verdict.DontReply) ? Concern.Strong
            : a.RulesScore > 0 || a.LinkProblems > 0 || a.SenderProblem || aiWorried ? Concern.Some
            : Concern.Unclear;

        var tier = new TierResult("Policy",
            state == MessageState.Cleared ? TierStatus.Passed : TierStatus.Flagged, note);
        return new Decision(state, summary, concern, a.RulesScore, flags, tier);
    }
}
