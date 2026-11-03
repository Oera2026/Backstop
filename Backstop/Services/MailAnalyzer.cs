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
        TierResult LinkTier);

    public sealed record Decision(
        MessageState State,
        string Summary,
        Concern Concern,
        int RulesScore,
        IReadOnlyList<Flag> Flags,
        TierResult PolicyTier);

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

        var top = found.OrderByDescending(f => MockRiskRules.Weight(f.Id)).Select(f => MockRiskRules.BecauseFor(f.Id)).FirstOrDefault();
        return new Analysis(flags, links, score, top, found.Any(f => f.Id == "at-risk"), deceptive, problems, senderProblem, senderSignals, rulesTier, linkTier);
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
        else if (a.SenderProblem) summary = a.SenderSignals.First(s => s.Severity == SenderSeverity.High).Reason;
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
