using Backstop.Models;
using Backstop.Services.Api;

namespace Backstop.Services;

/// <summary>
/// The local part of the checking pipeline: the Rules tier, the Link check tier and the Policy decision.
/// Everything here is worked out from the message itself. Nothing is scripted per message.
/// Links in real mail are judged by their address only and are never opened (the Link check service fetches links on the public /try page only).
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
        IReadOnlyList<SenderSignal> SenderSignals,
        TierResult RulesTier,
        TierResult LinkTier,
        // Ids of the warning signs found, used to pick the "watch for" tip: rules ids, link-hidden / link-odd, sender.
        IReadOnlyList<string> SignalIds);

    public sealed record Decision(
        MessageState State,
        string Summary,
        Concern Concern,
        int RulesScore,
        IReadOnlyList<Flag> Flags,
        TierResult PolicyTier,
        // The "Next time, watch for…" tip. Null when the message is Cleared; never empty otherwise.
        string? WatchFor = null);

    /// <summary>Title of the one-line flag a High sender signal adds. Screens that list the signals in full leave this flag out.</summary>
    public const string SenderFlagTitle = "The sender looks wrong";

    /// <summary>
    /// Rules tier, Link check tier and the Sender check. Pure and fast. <paramref name="contacts"/> are the recipient's saved contacts
    /// (so "Mum" on a new address can be spotted); with none, the contact check simply has nothing to compare.
    /// </summary>
    public static Analysis Analyze(InboundMail mail, IReadOnlyList<Contact>? contacts = null, ISenderCheck? senderCheck = null)
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

        // Sender: who it says it is from, against the address, the Reply-To and the saved contacts. A High signal holds the message.
        var senderSignals = (senderCheck ?? SenderCheck.Default).Check(mail, contacts ?? []);
        var senderProblem = SenderSignal.NeedsHold(senderSignals);
        if (senderProblem)
            flags.Add(new Flag("user", SenderFlagTitle, senderSignals.First(s => s.Severity == SenderSeverity.High).Reason));

        var signalIds = found.Select(f => f.Id).ToList();
        if (deceptive) signalIds.Add("link-hidden"); else if (problems > 0) signalIds.Add("link-odd");
        if (senderProblem) signalIds.Add("sender");

        var top = found.OrderByDescending(f => MockRiskRules.Weight(f.Id)).Select(f => MockRiskRules.BecauseFor(f.Id)).FirstOrDefault();
        return new Analysis(flags, links, score, top, found.Any(f => f.Id == "at-risk"), deceptive, problems, senderProblem, senderSignals, rulesTier, linkTier, signalIds);
    }

    /// <summary>
    /// For the public Try it page only. The links were opened, so what the Link check found replaces the guess made from the address
    /// alone (a shortener is no longer "hidden" once we know where it goes). Whether a link is bad is decided from the reports by the
    /// caller; here only the address-only link warnings are taken out, and a disguised link is kept for the "block" rule.
    /// </summary>
    public static Analysis WithInspectedLinks(Analysis a, IReadOnlyList<LinkReport> reports)
    {
        if (reports.Count == 0) return a;
        var deceptive = reports.Any(r => r.Disguised);
        var flags = a.Flags.Where(f => f.Title is not ("The link hides where it goes" or "The link looks unusual")).ToList();
        var ids = a.SignalIds.Where(i => i is not ("link-hidden" or "link-odd")).ToList();
        if (deceptive) ids.Add("link-hidden");
        var tier = new TierResult("Link check", reports.Any(r => r.Verdict != LinkVerdict.NoWarnings) ? TierStatus.Flagged : TierStatus.Passed,
            $"{reports.Count} link{(reports.Count == 1 ? "" : "s")} opened and checked.");
        return a with { Links = [], LinkProblems = 0, DeceptiveLink = deceptive, LinkTier = tier, Flags = flags, SignalIds = ids };
    }
    /// <summary>
    /// The sender signals as plain sentences for the AI. They are Backstop's own wording about the headers, not the sender's,
    /// and they can only make the AI's answer stricter.
    /// </summary>
    public static IReadOnlyList<string> SenderFindings(Analysis a) =>
        a.SenderSignals.Select(s => $"Sender check ({s.Severity.ToString().ToLowerInvariant()}): {s.Reason}").ToList();

    /// <summary>
    /// Policy tier. Cleared only when every local check is clean AND Meta AI answered and agreed.
    /// Blocked only for a disguised link together with a strong warning, and never when someone may be at risk.
    /// Everything else, including a missing or failed AI answer, is Held.
    /// </summary>
    /// <summary>
    /// Warning signs so serious that the message is blocked outright, with no helper and no AI needed. To add one, add its signal id here.
    /// "injection" is an email giving orders to the checker: nobody who writes that is sending a real message.
    /// </summary>
    public static readonly IReadOnlyList<string> SeriousSignals = ["injection"];

    public static bool IsSerious(Analysis a) => a.SignalIds.Any(id => SeriousSignals.Contains(id));

    /// <summary>
    /// Blocked by the local checks alone: a serious sign, or a disguised link with a strong warning. Never when someone may be at risk,
    /// because that always goes to a person. The AI can only make a result stricter, and this is already the strictest, so it is not asked.
    /// </summary>
    public static bool BlocksOutright(Analysis a) =>
        !a.AtRisk && (IsSerious(a) || (a.DeceptiveLink && MockRiskRules.VerdictFor(a.RulesScore) == Verdict.DontReply));

    public static Decision Decide(Analysis a, MetaScamAnalysis? ai)
    {
        var aiAnswered = ai is { Success: true };
        var aiWorried = aiAnswered && (ai!.Verdict != Verdict.LooksSafe || ai.NeedsHuman || ai.AtRisk);
        var atRisk = a.AtRisk || (ai?.AtRisk ?? false);
        var localClean = a.RulesScore == 0 && a.LinkProblems == 0 && !a.SenderProblem;
        var strong = MockRiskRules.VerdictFor(a.RulesScore) == Verdict.DontReply;

        MessageState state;
        string note;
        var serious = IsSerious(a);
        if ((serious || (a.DeceptiveLink && strong)) && !atRisk)
        { state = MessageState.Blocked; note = serious ? "It tries to tell the checker what to say. Blocked automatically." : "Hidden link plus a strong warning sign. Blocked automatically."; }
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
        else if (state == MessageState.Blocked && serious) summary = "This message tries to tell our checker what to say, so it was blocked.";
        else if (a.RulesScore > 0)
            summary = MockRiskRules.ReasonFor(MockRiskRules.VerdictFor(a.RulesScore), a.TopBecause);
        else if (aiWorried) summary = ai!.Reason;
        else if (a.LinkProblems > 0) summary = "A link in this message looks unusual.";
        else if (a.SenderProblem) summary = a.SenderSignals.First(s => s.Severity == SenderSeverity.High).Reason;
        else summary = "The automatic check could not finish, so please look at this message with care.";

        // Worked out from the checks that ran. Cleared mail claims nothing about risk.
        var concern = state == MessageState.Cleared ? Concern.None
            : a.RulesScore >= 3 || a.DeceptiveLink || serious || (aiWorried && ai!.Verdict == Verdict.DontReply) ? Concern.Strong
            : a.RulesScore > 0 || a.LinkProblems > 0 || a.SenderProblem || aiWorried ? Concern.Some
            : Concern.Unclear;

        var tier = new TierResult("Policy",
            state == MessageState.Cleared ? TierStatus.Passed : TierStatus.Flagged, note);
        // A tip for anything not cleared: the checker's if it is clean, else the fixed one for the strongest sign.
        var ids = a.SignalIds.Concat(aiWorried ? ai!.Flags.Select(f => f.Id) : []).ToList();
        var tip = state == MessageState.Cleared ? null : WatchFor.Choose(aiWorried ? ai!.WatchFor : null, ids, summary);
        return new Decision(state, summary, concern, a.RulesScore, flags, tier, tip);
    }
}
