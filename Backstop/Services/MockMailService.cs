using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api;
using Backstop.Services.Links;

namespace Backstop.Services;

/// <summary>MOCK user-side service with live Meta AI Dev API checking where available.</summary>
public sealed partial class MockMailService(MailStore store, IMetaScamChecker? metaChecker = null, ILinkInspector? linkInspector = null, ISenderCheck? senderCheck = null) : IMailService
{
    public event Action? Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    public IReadOnlyList<Scenario> Scenarios => MockData.Scenarios;

    public IReadOnlyList<MailMessage> GetInbox() =>
        store.Query(m => m.State == MessageState.Cleared);

    // Failed, Received and Scanning are never shown, but they are not "held by a helper" yet either.
    public int HeldCount => store.Query(m => m.State == MessageState.Held).Count;

    public MailMessage? GetClearedMessage(string id)
    {
        var m = store.Find(id);
        return m is { State: MessageState.Cleared } ? m : null;
    }

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardNumber();
    [GeneratedRegex(@"\b(\d{2})-(\d{2})-(\d{2})\b")]
    private static partial Regex SixDigitsDashed();
    [GeneratedRegex(@"\b\d{8}\b")]
    private static partial Regex AccountNumber();

    // MOCK: replace with the real outgoing data-loss check.
    public OutgoingCheck CheckOutgoing(string to, string subject, string body)
    {
        // Folded, so hidden characters and full-width digits cannot hide a card number.
        var text = InputGuard.Fold($"{subject}\n{body}");
        var reasons = new List<string>();
        if (HasCardNumber(text)) reasons.Add("It looks like a card number.");
        if (HasBankCode(text)) reasons.Add("It looks like a bank code.");
        if (SensitivePatterns.Secret.IsMatch(text)) reasons.Add("It looks like a password or secret code.");
        return new OutgoingCheck(reasons.Count > 0, reasons);
    }

    // Long numbers are only card numbers when they pass the card checksum (Luhn), so phone and order numbers are left alone.
    private static bool HasCardNumber(string text) =>
        CardNumber().Matches(text).Any(m => PassesLuhn(new string(m.Value.Where(char.IsDigit).ToArray())));

    private static bool PassesLuhn(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1) d = d * 2 > 9 ? d * 2 - 9 : d * 2;
            sum += d;
        }
        return sum % 10 == 0;
    }

    // 12-34-56 is a sort code, but 27-10-26 is a date. A dashed number that reads as a date only counts
    // when an account number or bank words are there too.
    private static bool HasBankCode(string text)
    {
        if (SensitivePatterns.Bank.IsMatch(text)) return true;
        var hasAccount = AccountNumber().IsMatch(text);
        return SixDigitsDashed().Matches(text).Any(m => hasAccount || !ReadsAsDate(m));
    }

    private static bool ReadsAsDate(Match m)
    {
        int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), c = int.Parse(m.Groups[3].Value);
        static bool Day(int n) => n is >= 1 and <= 31;
        static bool Month(int n) => n is >= 1 and <= 12;
        return (Day(a) && Month(b))   // 27-10-26 (day first)
            || (Month(a) && Day(b))   // 10-27-26 (month first)
            || (Month(b) && Day(c));  // 26-10-27 (year first)
    }

    public SendResult Send(string to, string subject, string body)
    {
        to = to.Trim();

        // One plain address, a one-line subject, a sensible size. Anything else is refused, never tidied up and sent.
        if (!InputGuard.IsPlainAddress(to) || !InputGuard.IsSingleLine(subject, 200) || (body?.Length ?? 0) > 20000)
            return new SendResult(null, false, "That message could not be sent. Please check the address and the subject.");

        // Fail closed: never write to an address nobody reads, whatever the UI did first.
        if (NoReply.IsNoReply(to))
            return new SendResult(null, false, "That is a no-reply address. Nobody reads messages sent to it.");

        var isNew = !store.IsKnownRecipient(to);
        var contact = store.Contacts().FirstOrDefault(c => c.Address.Equals(to, StringComparison.OrdinalIgnoreCase));
        var sent = new SentMessage
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ToName = contact?.Name ?? "",
            ToAddress = to,
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Body = body,
        };
        var notice = isNew
            ? new HelperNotice { Id = Guid.NewGuid().ToString("N")[..8], Address = to, Subject = sent.Subject }
            : null;

        // MOCK: the message is only stored in memory. Nothing is sent anywhere.
        store.AddSent(sent, notice);
        return new SendResult(sent, isNew, null);
    }

    public bool HoldOutgoing(string to, string subject, string body, IReadOnlyList<string> reasons)
    {
        to = to.Trim();
        if (body is null || !InputGuard.IsPlainAddress(to) || !InputGuard.IsSingleLine(subject, 200) || body.Length > 20000) return false; // fail closed: no body, nothing queued
        if (NoReply.IsNoReply(to)) return false; // fail closed: nobody reads it, so nothing is queued
        store.HoldOutgoing(to, subject, body, reasons);
        return true;
    }

    public IReadOnlyList<Contact> GetContacts() => store.Contacts().Where(c => !NoReply.IsNoReply(c.Address)).ToList();
    public Contact? GetContact(string id) => store.GetContact(id);
    public Contact? FindContact(string address) => store.FindContact(address);
    public ContactPhoto? GetContactPhoto(string id) => store.Photo(id);
    public void RemoveContactPhoto(string id) => store.SetPhoto(id, null);

    public bool SetContactPhoto(string id, byte[] bytes)
    {
        if (store.GetContact(id) is null) return false;
        // Trust the bytes, never the file name or the browser's claim. SVG is refused on purpose (it can carry script).
        var type = Sniff(bytes);
        if (type is null) return false;
        store.SetPhoto(id, new ContactPhoto(bytes, type));
        return true;
    }

    private static string? Sniff(byte[] b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "image/gif";
        if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    public IReadOnlyList<SentMessage> GetSent() => store.Sent();
    public SentMessage? GetSent(string id) => store.FindSent(id);

    // Only Cleared mail is searched (GetInbox), so held or blocked mail can never turn up in a search.
    public SearchResults Search(string query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0) return new SearchResults([], [], []);
        bool Has(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        return new SearchResults(
            GetInbox().Where(m => Has(m.FromName) || Has(m.Subject) || Has(m.Body)).ToList(),
            store.Sent().Where(s => Has(s.ToName) || Has(s.ToAddress) || Has(s.Subject) || Has(s.Body)).ToList(),
            GetContacts().Where(c => Has(c.Name) || Has(c.Address) || Has(c.Relationship)).ToList());
    }

    public IReadOnlyList<CalendarEvent> GetEvents() => store.Events();

    public void AddEvent(string title, DateOnly date, TimeOnly? time, string place) =>
        store.AddEvent(new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Title = title.Trim(),
            Date = date,
            Time = time,
            Place = place.Trim(),
        });

    public Task<MailMessage?> ReceiveAsync(string scenarioId, bool simulateFailure, Action<int, TierResult>? onTier = null)
    {
        var scenario = MockData.Scenarios.FirstOrDefault(s => s.Id == scenarioId);
        return scenario is null ? Task.FromResult<MailMessage?>(null) : ReceiveAsync(scenario.Mail, simulateFailure, onTier);
    }

    /// <summary>
    /// The real pipeline, in order: Rules, Meta AI, Link check, Policy. What happens to the message is worked out
    /// from its own text. Cleared needs every check to pass AND Meta AI to answer. Any error ends in Failed -> Held.
    /// </summary>
    public async Task<MailMessage?> ReceiveAsync(InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier = null)
    {
        var msg = MockData.CreateMessage(mail);
        store.Add(msg); // state Received: invisible to the user

        var done = new List<TierResult>();
        MailAnalyzer.Analysis? analysis = null;
        try
        {
            store.Update(() => msg.State = MessageState.Scanning);
            // The local result is worked out first and kept, so a failure later still leaves the helper the evidence.
            analysis = MailAnalyzer.Analyze(mail, store.Contacts(), senderCheck);

            // Tier 0: Rules.
            Step(0, onTier);
            done.Add(analysis.RulesTier);
            onTier?.Invoke(0, analysis.RulesTier);

            // Tier 1: Meta AI reads the email (text only, to save cost). A failure, a timeout, an odd answer or no
            // key all throw, and the catch below holds the message. Nothing is cleared without this answer.
            Step(1, onTier);
            if (simulateFailure) throw new InvalidOperationException("Simulated checker failure");
            if (metaChecker is null) throw new InvalidOperationException("No Meta AI checker is configured");
            var ai = await metaChecker.CheckScamWithEvidenceAsync($"{mail.Subject}\n\n{mail.Body}", [], MailAnalyzer.SenderFindings(analysis));
            if (!ai.Success)
                throw new InvalidOperationException("Meta AI check failed or timed out: " + (ai.FailureNote ?? "Unknown error"));
            var aiTier = new TierResult("Meta AI",
                ai.Verdict == Verdict.LooksSafe && !ai.NeedsHuman && !ai.AtRisk ? TierStatus.Passed : TierStatus.Flagged, ai.Reason);
            done.Add(aiTier);
            onTier?.Invoke(1, aiTier);

            // Tier 2: Link check (by address only).
            Step(2, onTier);
            done.Add(analysis.LinkTier);
            onTier?.Invoke(2, analysis.LinkTier);

            // Tier 3: Policy. The local checks set a floor. Meta AI can only make the result stricter.
            Step(3, onTier);
            var decision = MailAnalyzer.Decide(analysis, ai);
            done.Add(decision.PolicyTier);
            onTier?.Invoke(3, decision.PolicyTier);

            store.Update(() =>
            {
                msg.Flags = [.. decision.Flags];
                msg.Links = [.. analysis.Links];
                msg.SenderSignals = [.. analysis.SenderSignals];
                msg.Summary = decision.Summary;
                msg.Concern = decision.Concern;
                msg.RulesScore = decision.RulesScore;
                msg.Tiers = done;
                msg.State = decision.State;
                msg.ReceivedAt = DateTimeOffset.Now;
            });
        }
        catch (Exception)
        {
            try { FailClosed(msg, done, analysis, onTier); }
            catch (Exception)
            {
                // Last resort: even if recording the failure goes wrong, the message must not stay in Received or Scanning.
                try { store.Update(() => { msg.State = MessageState.Held; msg.AwaitingScan = true; msg.ReceivedAt = DateTimeOffset.Now; }); }
                catch (Exception) { msg.State = MessageState.Held; msg.AwaitingScan = true; }
            }
        }
        return msg;
    }

    // Tells a watcher (the demo panel) which check has started. There is no pause: the only waiting is the real Meta call.
    private static void Step(int i, Action<int, TierResult>? onTier)
    {
        onTier?.Invoke(i, new TierResult(MockData.TierNames[i], TierStatus.Running, "Checking…"));
    }

    /// <summary>
    /// Failed -> Held. An error never lets a message through. The local result (flags, links, at-risk signal)
    /// is kept, so the helper still sees what the local checks found.
    /// </summary>
    private void FailClosed(MailMessage msg, List<TierResult> done, MailAnalyzer.Analysis? analysis, Action<int, TierResult>? onTier)
    {
        var names = MockData.TierNames;
        var failedAt = done.Count;
        if (failedAt < names.Length)
        {
            var failed = new TierResult(names[failedAt], TierStatus.Failed, "The checker had a problem.");
            done.Add(failed);
            onTier?.Invoke(failedAt, failed);
        }

        const int linkTier = 2;
        for (var i = failedAt + 1; i < names.Length; i++)
        {
            var isPolicy = i == names.Length - 1;
            // The link check is local, so when the analysis exists its real result is shown, not "Not run".
            var r = isPolicy
                ? new TierResult(names[i], TierStatus.Flagged, analysis?.AtRisk == true
                    ? "Someone may need help, so a helper looks first."
                    : "Something went wrong, so a helper looks first.")
                : i == linkTier && analysis is not null
                    ? analysis.LinkTier
                    : new TierResult(names[i], TierStatus.Skipped, "Not run.");
            done.Add(r);
            onTier?.Invoke(i, r);
        }

        var local = analysis is null ? null : MailAnalyzer.Decide(analysis, null);
        store.Update(() =>
        {
            msg.State = MessageState.Failed;
            msg.AwaitingScan = true; // the AI did not answer, so it is scanned again until it does
            msg.Tiers = done;
            msg.Flags = [new Flag("alert", "The checker had a problem", "We could not finish checking this message, so it is held until a person looks at it."), .. local?.Flags ?? []];
            msg.Links = analysis is null ? [] : [.. analysis.Links];
            msg.SenderSignals = analysis is null ? [] : [.. analysis.SenderSignals];
            msg.Summary = local is not null && (analysis!.RulesScore + analysis.LinkProblems > 0 || analysis.AtRisk || analysis.SenderProblem)
                ? local.Summary
                : "The automatic check did not finish. Please look at this message with care.";
            msg.Concern = local?.Concern ?? Concern.Unclear;
            msg.RulesScore = analysis?.RulesScore ?? 0;
            msg.ReceivedAt = DateTimeOffset.Now;
            msg.State = MessageState.Held; // explicit Failed -> Held transition, never Blocked or Cleared
        });
    }

    private readonly object _scanGate = new();
    private readonly HashSet<string> _scanning = [];

    /// <summary>
    /// Mail held because the AI never answered is checked again here. It is only released, held for a helper or
    /// blocked when the AI answers, using the same policy as a new message. No answer leaves it exactly as it was.
    /// </summary>
    public async Task<int> RescanAsync(string? id = null)
    {
        if (metaChecker is null) return 0;
        var waiting = store.Query(m => m.State == MessageState.Held && m.AwaitingScan && (id is null || m.Id == id));
        var answered = 0;
        foreach (var msg in waiting)
        {
            lock (_scanGate) { if (!_scanning.Add(msg.Id)) continue; } // already being scanned
            try { if (await RescanOne(msg)) answered++; }
            catch (Exception) { /* no answer: stays held and is tried again later */ }
            finally { lock (_scanGate) _scanning.Remove(msg.Id); }
        }
        return answered;
    }

    private async Task<bool> RescanOne(MailMessage msg)
    {
        var mail = msg.AsInbound();
        var analysis = MailAnalyzer.Analyze(mail, store.Contacts(), senderCheck);
        var ai = await metaChecker!.CheckScamWithEvidenceAsync($"{mail.Subject}\n\n{mail.Body}", [], MailAnalyzer.SenderFindings(analysis));
        if (!ai.Success) return false;

        var aiTier = new TierResult("Meta AI",
            ai.Verdict == Verdict.LooksSafe && !ai.NeedsHuman && !ai.AtRisk ? TierStatus.Passed : TierStatus.Flagged, ai.Reason);
        var decision = MailAnalyzer.Decide(analysis, ai);
        // Only still-held, still-waiting mail changes. A helper who blocked it in the meantime wins.
        return store.TryChange(msg.Id, m => m.State == MessageState.Held && m.AwaitingScan, m =>
        {
            m.Flags = [.. decision.Flags];
            m.Links = [.. analysis.Links];
            m.SenderSignals = [.. analysis.SenderSignals];
            m.Summary = decision.Summary;
            m.Concern = decision.Concern;
            m.RulesScore = decision.RulesScore;
            m.Tiers = [analysis.RulesTier, aiTier, analysis.LinkTier, decision.PolicyTier];
            m.State = decision.State;
            m.AwaitingScan = false;
            m.ReceivedAt = DateTimeOffset.Now;
        });
    }

    public void Reset() => store.Reset();
}
