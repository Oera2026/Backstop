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

    /// <summary>All the links in one message share this much time. Each hop is also capped at 5 seconds by the inspector.</summary>
    internal TimeSpan LinkBudget { get; set; } = TimeSpan.FromSeconds(12);

    // Opening a link costs a request to somewhere else, so it has its own, tighter cap per visitor.
    internal MetaCallGuard LinkGuard { get; set; } = new(limit: 300, visitorLimit: 12);

    public IReadOnlyList<TrySample> TrySamples => TryData.Samples;

    public async Task<TryResult> TryCheckAsync(InboundMail mail, bool outgoing, CancellationToken ct = default)
    {
        var from = (mail.FromAddress ?? "").Trim();
        var subject = (mail.Subject ?? "").Trim();
        var body = (mail.Body ?? "").Trim();

        // A name sent apart from the address ("Mum", "mum@...") is the same as "Mum <mum@...>", the way the page sends it.
        var shownName = (mail.FromName ?? "").Trim();
        if (!outgoing && shownName.Length > 0 && shownName != from && !from.Contains('<')) from = $"{shownName} <{from}>";

        var replyTo = string.IsNullOrWhiteSpace(mail.ReplyTo) ? null : mail.ReplyTo.Trim();
        if (from.Length + subject.Length + body.Length + (replyTo?.Length ?? 0) > TryMaxChars)
            return TryResult.Refused($"That is too long. Please keep it under {TryMaxChars:N0} letters.");
        if (subject.Length == 0 && body.Length == 0)
            return TryResult.Refused("Please type a subject or a message.");
        if (outgoing && !InputGuard.IsPlainAddress(from))
            return TryResult.Refused("Please type one email address to send to.");
        if (outgoing && !InputGuard.IsSingleLine(subject, 200))
            return TryResult.Refused("The subject must be one short line.");
        if (!TryGuard.TryEnter(ClientIdentity.Current ?? "unknown", out var wait, out _, out _))
            return TryResult.Refused($"That is a lot of checks. Please wait {Math.Max(1, wait)} seconds and try again.");

        return outgoing ? await TryOutgoingAsync(from, subject, body, ct) : await TryIncomingAsync(from, subject, body, replyTo, ct);
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
    private async Task<MetaScamAnalysis?> AskMetaAsync(string subject, string body, IReadOnlyList<string> linkFindings, CancellationToken ct, IReadOnlyList<string>? senderFindings = null)
    {
        if (metaChecker is null) return null;
        try
        {
            var text = $"{subject}\n\n{body}";
            var ai = senderFindings is { Count: > 0 }
                ? await metaChecker.CheckScamWithEvidenceAsync(text, linkFindings, senderFindings, ct)
                : linkFindings.Count > 0
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
        // The whole link check has one budget, so a slow chain of redirects can never use up the time the AI check needs afterwards.
        // Any link still open when it runs out is "could not verify", which holds the message exactly as before.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(LinkBudget);
        var tasks = links.Take(LinkInspector.MaxLinksChecked).Select(async l =>
        {
            try
            {
                if (!LinkGuard.TryEnter(ClientIdentity.Current ?? "unknown", out _, out _, out _))
                    return Unchecked(l.Url, "There have been a lot of link checks. Please try again in a minute.");
                return await linkInspector.InspectAsync(l.Url, l.Shown, budget.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return Unchecked(l.Url, budget.IsCancellationRequested ? "It took too long to check." : "It could not be checked.");
            }
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
            // Harmless notes are labelled, so the checker does not take a shortener or a redirect for a warning.
            var harmless = r.Info is { Count: > 0 } ? $" Harmless notes: {string.Join(" ", r.Info)}" : "";
            return $"Link {r.StartUrl}: {r.Summary} {string.Join(" ", r.Signals)}{trip}{harmless}".Trim();
        }).ToList();
        if (notChecked > 0) list.Add($"{notChecked} more link{(notChecked == 1 ? " was" : "s were")} not checked, so treat {(notChecked == 1 ? "it" : "them")} as unsafe.");
        return list;
    }

    private async Task<TryResult> TryIncomingAsync(string from, string subject, string body, string? replyTo, CancellationToken ct)
    {
        // "Name <address>" or just an address. The same sender check as real mail compares the two.
        var open = from.LastIndexOf('<');
        var (name, address) = open >= 0 && from.EndsWith('>') ? (from[..open].Trim(), from[(open + 1)..^1].Trim()) : (from, from);
        var mail = new InboundMail(name.Length == 0 ? address : name, address, subject, body, replyTo);

        // The demo household's saved contacts stand in for the visitor's address book. They are made-up people.
        var analysis = MailAnalyzer.Analyze(mail, store.Contacts(), senderCheck);
        var result = await TryIncomingCoreAsync(analysis, subject, body, ct);
        return result with { SenderChecks = analysis.SenderSignals };
    }

    private async Task<TryResult> TryIncomingCoreAsync(MailAnalyzer.Analysis analysis, string subject, string body, CancellationToken ct)
    {
        // A serious warning sign sends the message to a helper on its own, so nothing is opened and nothing is sent to Meta.
        var serious = MailAnalyzer.IsSerious(analysis);
        var (reports, notChecked) = serious ? ([], 0) : await InspectLinksAsync($"{subject}\n{body}", ct);
        // The links were opened, so what was found replaces the guess made from the address alone. Without that, a harmless shortener
        // would still be called "hidden" and hold the message by itself, however clean the trip turned out to be.
        var byAddressOnly = analysis;
        analysis = MailAnalyzer.WithInspectedLinks(analysis, reports);
        // Blocked or held by the local checks alone: no AI answer could change that, so it is not asked and the answer is instant.
        var ai = MailAnalyzer.SkipsAi(analysis) ? null
            : await AskMetaAsync(subject, body, reports.Count > 0 ? Findings(reports, notChecked) : [], ct, MailAnalyzer.SenderFindings(analysis));
        // Same decision as real mail. With no AI answer it is Held, as a real message would be.
        var decision = MailAnalyzer.Decide(analysis, ai);
        // The sender warnings have their own "Sender checks" section, so the one-line flag for them is not repeated.
        var signals = Signals(decision.Flags.Where(f => f.Title != MailAnalyzer.SenderFlagTitle).ToList(), analysis.Links);

        // A link that is unsafe, or could not be checked, means the message cannot be called safe.
        var linkBad = reports.Any(r => r.Verdict != LinkVerdict.NoWarnings) || notChecked > 0;
        foreach (var r in reports.Where(r => r.Verdict != LinkVerdict.NoWarnings))
            signals.Add(new($"Link {r.StartUrl}", r.Summary));
        if (notChecked > 0) signals.Add(new("More links", $"{notChecked} more link{(notChecked == 1 ? " was" : "s were")} not checked, so they are treated as unsafe."));

        // "Next time, watch for…" for anything that is not Safe: the checker's tip if clean, else the fixed one. Safe gets none.
        var tipIds = analysis.SignalIds.Concat(ai?.Flags.Select(f => f.Id) ?? []).Concat(linkBad ? ["link"] : []).ToList();
        // The tip comes from a signal that fired. Harmless link notes (a shortener, a redirect) never fire one. When the checker
        // itself failed, that is a hold with no lesson in it: a tip is shown only if a real warning was found by the local checks.
        TryResult Tipped(TryResult r)
        {
            if (r.Verdict == TryVerdict.Safe && r.Verified) return r;
            if (ai is null)
            {
                var fired = analysis.SignalIds.Concat(linkBad ? ["link"] : []).ToList();
                return fired.Count == 0 ? r : r with { WatchFor = WatchFor.Fallback(fired) };
            }
            return r with { WatchFor = decision.WatchFor ?? WatchFor.Choose(ai.WatchFor, tipIds, r.Explanation) };
        }

        if (ai is null && decision.State == MessageState.Blocked)
            return Tipped(new(TryVerdict.Blocked, true, signals, decision.Summary,
                "It would be in their Blocked list. You would never see it.", Links: reports));

        // Held for a serious sign: the checks finished, and the answer is certain, so it counts as verified.
        if (ai is null && serious)
            return Tipped(new(TryVerdict.Held, true, signals, decision.Summary,
                "They would see it with these warnings, and choose Approve, Block or Call them first. You would not see it yet.", Links: reports));

        if (ai is null)
            return Tipped(new(TryVerdict.Held, false, signals, "Couldn't verify, so this would be held.",
                "Your helper would see it with any warnings found so far, and choose Approve, Block or Call them first.", Links: reports));

        var aiWorried = ai.Worried;
        var state = decision.State == MessageState.Cleared && linkBad ? MessageState.Held : decision.State;
        var explanation = state == MessageState.Held && decision.State == MessageState.Cleared && !aiWorried
            ? (reports.Any(r => r.Verdict == LinkVerdict.Unsafe) || notChecked > 0
                ? "A link in this message looks unsafe, or could not be checked, so it would be held."
                : "A link in this message needs a careful look, so it would be held.")
            : decision.State == MessageState.Cleared || aiWorried
                ? (string.IsNullOrWhiteSpace(ai.Reason) ? decision.Summary : ai.Reason)
                : decision.Summary;

        return Tipped(state switch
        {
            // Only this page opens links. Real mail judges a link by its address, so it may still wait for the helper.
            MessageState.Cleared => new(TryVerdict.Safe, true, signals, explanation,
                reports.Count > 0 && MailAnalyzer.Decide(byAddressOnly, ai).State != MessageState.Cleared
                    ? "If this came as real mail, they would check the link first."
                    : "Nothing. It would go straight to your inbox.", Links: reports),
            MessageState.Blocked => new(TryVerdict.Blocked, true, signals, explanation,
                "It would be in their Blocked list. You would never see it.", Links: reports),
            _ => new(TryVerdict.Held, true, signals, explanation,
                "They would see it with these warnings, and choose Approve, Block or Call them first. You would not see it yet.", Links: reports),
        });
    }

    private async Task<TryResult> TryOutgoingAsync(string to, string subject, string body, CancellationToken ct)
    {
        // The same private-details check Compose uses, plus Meta AI reading the draft. Nothing is sent or queued.
        // Links in a draft are not opened: only a message that is coming in is followed up.
        var check = CheckOutgoing(to, subject, body);
        var ai = await AskMetaAsync(subject, body, [], ct);
        var signals = check.Reasons.Select(r => new TrySignal(r.TrimEnd('.'), "")).ToList();
        var aiWorried = ai is { Worried: true };
        if (aiWorried)
            foreach (var f in ai!.Flags)
                if (signals.All(s => s.Title != f.Title)) signals.Add(new(f.Title, f.Explanation));

        if (ai is null)
            return new(TryVerdict.Held, false, signals, "Couldn't verify, so this would be held.",
                "Your helper would check it before anything is sent.", WatchFor: check.NeedsPause ? WatchFor.Tips["private"] : null);
        if (check.NeedsPause || aiWorried)
            return new(TryVerdict.Held, true, signals,
                aiWorried && !string.IsNullOrWhiteSpace(ai.Reason) ? ai.Reason : "This looks like something private, so it is best to check first.",
                "They would check it first, and nothing would be sent until they say so.",
                WatchFor: WatchFor.Choose(ai.WatchFor, ["private"], aiWorried ? ai.Reason : null));
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
