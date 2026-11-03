using Backstop.Models;
using Backstop.Services.Api;
using Backstop.Services.Links;

namespace Backstop.Services;

// The public Try it page. It runs the SAME checks as real mail (MailAnalyzer, the Meta checker, CheckOutgoing)
// but keeps nothing: no message is added to the store and nothing is saved.
// Only here are links opened (ILinkInspector). Real mail (ReceiveAsync) judges links by address and never opens them.
public sealed partial class MockMailService
{
    /// <summary>From, subject and body together. Longer text is refused, never cut, because a cut message cannot be called safe.</summary>
    public const int TryMaxChars = 5000;

    // A check on this page costs a Meta call, so each visitor gets a few a minute. The call to Meta has its own caps too.
    internal MetaCallGuard TryGuard { get; set; } = new(limit: 600, visitorLimit: 6);

    // Opening a link costs a request to somewhere else, so it has its own, tighter cap per visitor.
    internal MetaCallGuard LinkGuard { get; set; } = new(limit: 300, visitorLimit: 12);

    public IReadOnlyList<TrySample> TrySamples => TryData.Samples;

    public async Task<TryResult> TryCheckAsync(InboundMail mail, bool outgoing, CancellationToken ct = default)
    {
        var from = (mail.FromAddress ?? "").Trim();
        var subject = (mail.Subject ?? "").Trim();
        var body = (mail.Body ?? "").Trim();

        if (from.Length + subject.Length + body.Length > TryMaxChars)
            return TryResult.Refused($"That is too long. Please keep it under {TryMaxChars:N0} letters.");
        if (subject.Length == 0 && body.Length == 0)
            return TryResult.Refused("Please type a subject or a message.");
        if (outgoing && !InputGuard.IsPlainAddress(from))
            return TryResult.Refused("Please type one email address to send to.");
        if (outgoing && !InputGuard.IsSingleLine(subject, 200))
            return TryResult.Refused("The subject must be one short line.");
        if (!TryGuard.TryEnter(ClientIdentity.Current ?? "unknown", out var wait, out _, out _))
            return TryResult.Refused($"That is a lot of checks. Please wait {Math.Max(1, wait)} seconds and try again.");

        return outgoing ? await TryOutgoingAsync(from, subject, body, ct) : await TryIncomingAsync(from, subject, body, ct);
    }

    /// <summary>A single link, with the words shown for it if there are any. Goes through the same checks as a message that holds only that link.</summary>
    public Task<TryResult> TryCheckLinkAsync(string url, string? shownText, CancellationToken ct = default)
    {
        url = (url ?? "").Trim();
        shownText = shownText?.Trim();
        if (url.Length == 0) return Task.FromResult(TryResult.Refused("Please type a link."));
        if (url.Length > 2000 || (shownText?.Length ?? 0) > 300 || url.Any(char.IsWhiteSpace))
            return Task.FromResult(TryResult.Refused("Please type one link, with no spaces."));
        if (!url.Contains("://")) url = "https://" + url;
        var body = string.IsNullOrWhiteSpace(shownText) ? url : $"[{shownText.Replace("]", "").Replace("\n", " ")}]({url})";
        return TryCheckAsync(new InboundMail("Link check", "link-check@example.test", "Link to check", body), false, ct);
    }

    // Meta AI's answer, or null on any failure, a missing key or an odd reply. Null always means "could not verify".
    private async Task<MetaScamAnalysis?> AskMetaAsync(string subject, string body, IReadOnlyList<string> linkFindings, CancellationToken ct)
    {
        if (metaChecker is null) return null;
        try
        {
            var text = $"{subject}\n\n{body}";
            var ai = linkFindings.Count > 0
                ? await metaChecker.CheckScamWithLinksAsync(text, linkFindings, ct)
                : await metaChecker.CheckScamAsync(text, null, null, ct);
            return ai.Success ? ai : null;
        }
        catch (Exception) { return null; }
    }

    // Opens up to three links in the text (never more), one visitor cap each. Anything that goes wrong is "could not verify".
    private async Task<(List<LinkReport> Reports, int NotChecked)> InspectLinksAsync(string text, CancellationToken ct)
    {
        var reports = new List<LinkReport>();
        if (linkInspector is null) return (reports, 0);
        var links = LinkInspector.ExtractLinks(text);
        var tasks = links.Take(LinkInspector.MaxLinksChecked).Select(async l =>
        {
            try
            {
                if (!LinkGuard.TryEnter(ClientIdentity.Current ?? "unknown", out _, out _, out _))
                    return Unchecked(l.Url, "There have been a lot of link checks. Please try again in a minute.");
                return await linkInspector.InspectAsync(l.Url, l.Shown, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return Unchecked(l.Url, "It could not be checked."); }
        }).ToList();
        reports.AddRange(await Task.WhenAll(tasks));
        return (reports, Math.Max(0, links.Count - LinkInspector.MaxLinksChecked));
    }

    private static LinkReport Unchecked(string url, string why) =>
        new(Defang.Url(url.Length > LinkFinder.MaxLength ? url[..LinkFinder.MaxLength] : url), null, [], [why], LinkVerdict.Unsafe,
            Verified: false, Fetched: false, "Could not verify, treated as unsafe.");

    // One plain sentence per link, for the AI to explain in its own words.
    private static List<string> Findings(IReadOnlyList<LinkReport> reports, int notChecked)
    {
        var list = reports.Select(r =>
        {
            var trip = r.Chain.Count > 1 ? $" It goes through {r.Chain.Count} addresses and ends at {r.FinalUrl ?? "an unknown place"}." : "";
            return $"Link {r.StartUrl}: {r.Summary} {string.Join(" ", r.Signals)}{trip}".Trim();
        }).ToList();
        if (notChecked > 0) list.Add($"{notChecked} more link{(notChecked == 1 ? " was" : "s were")} not checked, so treat {(notChecked == 1 ? "it" : "them")} as unsafe.");
        return list;
    }

    private async Task<TryResult> TryIncomingAsync(string from, string subject, string body, CancellationToken ct)
    {
        // "Name <address>" or just an address. The same sender check as real mail compares the two.
        var open = from.LastIndexOf('<');
        var (name, address) = open >= 0 && from.EndsWith('>') ? (from[..open].Trim(), from[(open + 1)..^1].Trim()) : (from, from);
        var mail = new InboundMail(name.Length == 0 ? address : name, address, subject, body);

        var analysis = MailAnalyzer.Analyze(mail);
        var (reports, notChecked) = await InspectLinksAsync($"{subject}\n{body}", ct);
        var ai = await AskMetaAsync(subject, body, reports.Count > 0 ? Findings(reports, notChecked) : [], ct);
        // Same decision as real mail. With no AI answer it is Held, as a real message would be.
        var decision = MailAnalyzer.Decide(analysis, ai);
        var signals = Signals(decision.Flags, analysis.Links);

        // A link that is unsafe, or could not be checked, means the message cannot be called safe.
        var linkBad = reports.Any(r => r.Verdict != LinkVerdict.NoWarnings) || notChecked > 0;
        foreach (var r in reports.Where(r => r.Verdict != LinkVerdict.NoWarnings))
            signals.Add(new($"Link {r.StartUrl}", r.Summary));
        if (notChecked > 0) signals.Add(new("More links", $"{notChecked} more link{(notChecked == 1 ? " was" : "s were")} not checked, so they are treated as unsafe."));

        if (ai is null)
            return new(TryVerdict.Held, false, signals, "Couldn't verify, so this would be held.",
                "Your helper would see it with any warnings found so far, and choose Approve, Block or Call them first.", Links: reports);

        var aiWorried = ai.Verdict != Verdict.LooksSafe || ai.NeedsHuman || ai.AtRisk;
        var state = decision.State == MessageState.Cleared && linkBad ? MessageState.Held : decision.State;
        var explanation = state == MessageState.Held && decision.State == MessageState.Cleared && !aiWorried
            ? (reports.Any(r => r.Verdict == LinkVerdict.Unsafe) || notChecked > 0
                ? "A link in this message looks unsafe, or could not be checked, so it would be held."
                : "A link in this message needs a careful look, so it would be held.")
            : decision.State == MessageState.Cleared || aiWorried
                ? (string.IsNullOrWhiteSpace(ai.Reason) ? decision.Summary : ai.Reason)
                : decision.Summary;

        return state switch
        {
            MessageState.Cleared => new(TryVerdict.Safe, true, signals, explanation,
                "Nothing. It would go straight to your inbox.", Links: reports),
            MessageState.Blocked => new(TryVerdict.Blocked, true, signals, explanation,
                "It would be in their Blocked list. You would never see it.", Links: reports),
            _ => new(TryVerdict.Held, true, signals, explanation,
                "They would see it with these warnings, and choose Approve, Block or Call them first. You would not see it yet.", Links: reports),
        };
    }

    private async Task<TryResult> TryOutgoingAsync(string to, string subject, string body, CancellationToken ct)
    {
        // The same private-details check Compose uses, plus Meta AI reading the draft. Nothing is sent or queued.
        // Links in a draft are not opened: only a message that is coming in is followed up.
        var check = CheckOutgoing(to, subject, body);
        var ai = await AskMetaAsync(subject, body, [], ct);
        var signals = check.Reasons.Select(r => new TrySignal(r.TrimEnd('.'), "")).ToList();
        var aiWorried = ai is not null && (ai.Verdict != Verdict.LooksSafe || ai.NeedsHuman || ai.AtRisk);
        if (aiWorried)
            foreach (var f in ai!.Flags)
                if (signals.All(s => s.Title != f.Title)) signals.Add(new(f.Title, f.Explanation));

        if (ai is null)
            return new(TryVerdict.Held, false, signals, "Couldn't verify, so this would be held.",
                "Your helper would check it before anything is sent.");
        if (check.NeedsPause || aiWorried)
            return new(TryVerdict.Held, true, signals,
                aiWorried && !string.IsNullOrWhiteSpace(ai.Reason) ? ai.Reason : "This looks like something private, so it is best to check first.",
                "They would check it first, and nothing would be sent until they say so.");
        return new(TryVerdict.Safe, true, signals, string.IsNullOrWhiteSpace(ai.Reason) ? "Nothing unusual found." : ai.Reason,
            "Nothing. It would be sent.");
    }

    // Flag titles first, then what the address checks found for each link. Links are shown defanged, never as links.
    private static List<TrySignal> Signals(IReadOnlyList<Flag> flags, IReadOnlyList<LinkCheck> links)
    {
        var list = flags.Select(f => new TrySignal(f.Title, f.Explanation)).ToList();
        foreach (var l in links)
            foreach (var s in l.Signals)
                list.Add(new($"Link {l.DefangedUrl}", s));
        return list;
    }
}
