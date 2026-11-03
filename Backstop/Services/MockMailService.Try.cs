using Backstop.Models;
using Backstop.Services.Api;

namespace Backstop.Services;

// The public Try it page. It runs the SAME checks as real mail (MailAnalyzer, the Meta checker, CheckOutgoing)
// but keeps nothing: no message is added to the store, nothing is saved, and links are read as text, never opened.
public sealed partial class MockMailService
{
    /// <summary>From, subject and body together. Longer text is refused, never cut, because a cut message cannot be called safe.</summary>
    public const int TryMaxChars = 5000;

    // A check on this page costs a Meta call, so each visitor gets a few a minute. The call to Meta has its own caps too.
    internal MetaCallGuard TryGuard { get; set; } = new(limit: 600, visitorLimit: 6);

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

    // Meta AI's answer, or null on any failure, a missing key or an odd reply. Null always means "could not verify".
    private async Task<MetaScamAnalysis?> AskMetaAsync(string subject, string body, CancellationToken ct)
    {
        if (metaChecker is null) return null;
        try
        {
            var ai = await metaChecker.CheckScamAsync($"{subject}\n\n{body}", null, null, ct);
            return ai.Success ? ai : null;
        }
        catch (Exception) { return null; }
    }

    private async Task<TryResult> TryIncomingAsync(string from, string subject, string body, CancellationToken ct)
    {
        // "Name <address>" or just an address. The same sender check as real mail compares the two.
        var open = from.LastIndexOf('<');
        var (name, address) = open >= 0 && from.EndsWith('>') ? (from[..open].Trim(), from[(open + 1)..^1].Trim()) : (from, from);
        var mail = new InboundMail(name.Length == 0 ? address : name, address, subject, body);

        var analysis = MailAnalyzer.Analyze(mail);
        var ai = await AskMetaAsync(subject, body, ct);
        // Same decision as real mail. With no AI answer it is Held, as a real message would be.
        var decision = MailAnalyzer.Decide(analysis, ai);
        var signals = Signals(decision.Flags, analysis.Links);

        if (ai is null)
            return new(TryVerdict.Held, false, signals, "Couldn't verify, so this would be held.",
                "Your helper would see it with any warnings found so far, and choose Approve, Block or Call them first.");

        var aiWorried = ai.Verdict != Verdict.LooksSafe || ai.NeedsHuman || ai.AtRisk;
        var explanation = decision.State == MessageState.Cleared || aiWorried
            ? (string.IsNullOrWhiteSpace(ai.Reason) ? decision.Summary : ai.Reason)
            : decision.Summary;

        return decision.State switch
        {
            MessageState.Cleared => new(TryVerdict.Safe, true, signals, explanation,
                "Nothing. It would go straight to your inbox."),
            MessageState.Blocked => new(TryVerdict.Blocked, true, signals, explanation,
                "It would be in their Blocked list. You would never see it."),
            _ => new(TryVerdict.Held, true, signals, explanation,
                "They would see it with these warnings, and choose Approve, Block or Call them first. You would not see it yet."),
        };
    }

    private async Task<TryResult> TryOutgoingAsync(string to, string subject, string body, CancellationToken ct)
    {
        // The same private-details check Compose uses, plus Meta AI reading the draft. Nothing is sent or queued.
        var check = CheckOutgoing(to, subject, body);
        var ai = await AskMetaAsync(subject, body, ct);
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
