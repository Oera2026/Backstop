using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api;
using Backstop.Services.Links;

namespace Backstop.Services;

/// <summary>MOCK user-side service with live Meta AI Dev API checking where available.</summary>
public sealed partial class MockMailService(MailStore store, IMetaScamChecker? metaChecker = null, ILinkInspector? linkInspector = null, ISenderCheck? senderCheck = null, ILogger<MockMailService>? logger = null) : IMailService
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
    public int HeldCount => store.Count(m => m.State == MessageState.Held);

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
        var contact = store.FindContact(to);
        var sent = new SentMessage
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ToName = contact?.Name ?? "",
            ToAddress = to,
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Body = body ?? "",
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
        // The page caps uploads too; this is the real limit, so a caller cannot store more.
        if (bytes is null || bytes.Length > MaxContactPhotoBytes) return false;
        var type = ImageSniff.ImageType(bytes);
        if (type is null) return false;
        store.SetPhoto(id, new ContactPhoto(bytes, type));
        return true;
    }

    public const int MaxContactPhotoBytes = 3 * 1024 * 1024;

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
    // Each visitor can add a few messages a minute, so nobody can fill the shared queue and push other held mail out.
    internal MetaCallGuard ReceiveGuard { get; set; } = new(limit: 600, visitorLimit: 12);

    /// <summary>Subject and body together. Longer text is refused, never checked, like the page limits.</summary>
    internal const int ReceiveMaxChars = 20000;

    public async Task<MailMessage?> ReceiveAsync(InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier = null)
    {
        if (!MayReceive(mail)) return null;
        var msg = MockData.CreateMessage(mail);
        store.Add(msg); // state Received: invisible to the user
        await RunChecksAsync(msg, mail, simulateFailure, onTier);
        return store.Find(msg.Id) ?? msg;
    }

    public async Task<MessageState?> CheckOnceAsync(InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier = null)
    {
        if (!MayReceive(mail)) return null;
        // Never added to the store, so no inbox, queue or rescan ever holds it. Only the outcome leaves this method.
        var msg = MockData.CreateMessage(mail);
        await RunChecksAsync(msg, mail, simulateFailure, onTier);
        return msg.State is MessageState.Cleared or MessageState.Blocked ? msg.State : MessageState.Held;
    }

    private bool MayReceive(InboundMail mail) =>
        mail.Subject.Length + mail.Body.Length <= ReceiveMaxChars
        && ReceiveGuard.TryEnter(ClientIdentity.Current ?? "anonymous", out _, out _, out _);

    private async Task RunChecksAsync(MailMessage msg, InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier)
    {
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
            Notify(onTier, 0, analysis.RulesTier);

            // Tier 1: Meta AI reads the email (text only, to save cost). A failure, a timeout, an odd answer or no
            // key all throw, and the catch below holds the message. Nothing is cleared without this answer.
            Step(1, onTier);
            MetaScamAnalysis? ai = null;
            TierResult aiTier;
            if (MailAnalyzer.SkipsAi(analysis))
            {
                // No AI answer could change the outcome, so it is not asked: the answer is instant and the text never leaves the app.
                aiTier = new TierResult("Meta AI", TierStatus.Skipped, MailAnalyzer.BlocksOutright(analysis)
                    ? "Not needed: the other checks already block this."
                    : "Not needed: a helper looks at this whatever the AI says.");
            }
            else
            {
                if (simulateFailure) throw new InvalidOperationException("Simulated checker failure");
                if (metaChecker is null) throw new InvalidOperationException("No Meta AI checker is configured");
                ai = await metaChecker.CheckScamWithEvidenceAsync($"{mail.Subject}\n\n{mail.Body}", [], MailAnalyzer.SenderFindings(analysis));
                if (!ai.Success)
                    throw new InvalidOperationException("Meta AI check failed or timed out: " + (ai.FailureNote ?? "Unknown error"));
                aiTier = AiTier(ai);
            }
            done.Add(aiTier);
            Notify(onTier, 1, aiTier);

            // Tier 2: Link check (by address only).
            Step(2, onTier);
            done.Add(analysis.LinkTier);
            Notify(onTier, 2, analysis.LinkTier);

            // Tier 3: Policy. The local checks set a floor. Meta AI can only make the result stricter.
            Step(3, onTier);
            var decision = MailAnalyzer.Decide(analysis, ai);
            done.Add(decision.PolicyTier);
            Notify(onTier, 3, decision.PolicyTier);

            store.Update(() => Apply(msg, analysis, decision, done));
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
    }

    private static TierResult AiTier(MetaScamAnalysis ai) =>
        new("Meta AI", ai.Worried ? TierStatus.Flagged : TierStatus.Passed, ai.Reason);

    // The finished result of a check, written onto the message. The caller holds the store's lock.
    private static void Apply(MailMessage m, MailAnalyzer.Analysis analysis, MailAnalyzer.Decision decision, List<TierResult> tiers)
    {
        m.Flags = [.. decision.Flags];
        m.Links = [.. analysis.Links];
        m.SenderSignals = [.. analysis.SenderSignals];
        m.Summary = decision.Summary;
        m.Concern = decision.Concern;
        m.WatchFor = decision.WatchFor;
        m.RulesScore = decision.RulesScore;
        m.Tiers = tiers;
        m.State = decision.State;
        m.AwaitingScan = false;
        m.ReceivedAt = DateTimeOffset.Now;
    }

    // One watcher callback, guarded. A throwing observer is logged (tier number only, never message content)
    // and the check carries on: the watcher must not decide the outcome.
    private void Notify(Action<int, TierResult>? onTier, int tier, TierResult result)
    {
        if (onTier is null) return;
        try { onTier(tier, result); }
        catch (Exception ex) { logger?.LogWarning(ex, "Mail tier observer failed for tier {Tier}.", tier); }
    }

    // Tells a watcher (the demo panel) which check has started. There is no pause: the only waiting is the real Meta call.
    private void Step(int i, Action<int, TierResult>? onTier)
    {
        Notify(onTier, i, new TierResult(MockData.TierNames[i], TierStatus.Running, "Checking…"));
    }

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
            Notify(onTier, failedAt, failed);
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
            Notify(onTier, i, r);
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
            msg.WatchFor = local?.WatchFor ?? WatchFor.Fallback(analysis?.SignalIds);
            msg.RulesScore = analysis?.RulesScore ?? 0;
            msg.ReceivedAt = DateTimeOffset.Now;
            msg.State = MessageState.Held; // explicit Failed -> Held transition, never Blocked or Cleared
        });
    }

    private readonly object _scanGate = new();
    private readonly HashSet<string> _scanning = [];
    // Timer rescans of one message back off after each miss (45s, 90s, 3m … up to 30m), so a Meta outage does not
    // spend the daily cap on the same few messages. A helper's "Check again" always tries at once.
    private readonly Dictionary<string, (int Misses, DateTimeOffset NextTry)> _backoff = [];
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    // A helper who has a decision waiting out its undo window, or who said they would call first, is in charge of this message.
    // The AI must not change it under them.
    private static bool MayRescan(MailMessage m) =>
        m.State == MessageState.Held && m.AwaitingScan && !m.DecisionPending && !m.CallRequested;

    /// <summary>
    /// Mail held because the AI never answered is checked again here. It is only released, held for a helper or
    /// blocked when the AI answers, using the same policy as a new message. No answer leaves it exactly as it was.
    /// <paramref name="id"/> null is the timer's sweep, which skips messages still backing off.
    /// </summary>
    public async Task<int> RescanAsync(string? id = null)
    {
        if (metaChecker is null) return 0;
        var now = Clock();
        var waiting = store.Query(m => MayRescan(m) && (id is null || m.Id == id));
        var answered = 0;
        foreach (var msg in waiting)
        {
            lock (_scanGate)
            {
                if (id is null && _backoff.TryGetValue(msg.Id, out var b) && now < b.NextTry) continue;
                if (!_scanning.Add(msg.Id)) continue; // already being scanned
            }
            var ok = false;
            try { ok = await RescanOne(msg); }
            catch (Exception) { /* no answer: stays held and is tried again later */ }
            finally
            {
                lock (_scanGate)
                {
                    _scanning.Remove(msg.Id);
                    if (ok) _backoff.Remove(msg.Id);
                    else
                    {
                        var misses = _backoff.GetValueOrDefault(msg.Id).Misses + 1;
                        var wait = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 45 * Math.Pow(2, misses - 1)));
                        _backoff[msg.Id] = (misses, Clock() + wait);
                    }
                    // Forget messages that are no longer waiting, so the table stays small.
                    if (_backoff.Count > 200)
                        foreach (var gone in _backoff.Keys.Where(k => store.Find(k) is not { AwaitingScan: true }).ToList()) _backoff.Remove(gone);
                }
            }
            if (ok) answered++;
        }
        return answered;
    }

    private async Task<bool> RescanOne(MailMessage msg)
    {
        var mail = msg.AsInbound();
        var analysis = MailAnalyzer.Analyze(mail, store.Contacts(), senderCheck);
        if (MailAnalyzer.SkipsAi(analysis))
        {
            // Same as a new message: no AI answer could change the outcome, so the text never leaves the app.
            var local = MailAnalyzer.Decide(analysis, null);
            var skipped = new TierResult("Meta AI", TierStatus.Skipped, MailAnalyzer.BlocksOutright(analysis)
                ? "Not needed: the other checks already block this."
                : "Not needed: a helper looks at this whatever the AI says.");
            return store.TryChange(msg.Id, MayRescan, m => Apply(m, analysis, local, [analysis.RulesTier, skipped, analysis.LinkTier, local.PolicyTier]));
        }
        var ai = await metaChecker!.CheckScamWithEvidenceAsync($"{mail.Subject}\n\n{mail.Body}", [], MailAnalyzer.SenderFindings(analysis));
        if (!ai.Success) return false;

        var decision = MailAnalyzer.Decide(analysis, ai);
        // Only still-held, still-waiting mail nobody is deciding changes. A helper who blocked it, is deciding it, or will call first wins.
        return store.TryChange(msg.Id, MayRescan, m => Apply(m, analysis, decision, [analysis.RulesTier, AiTier(ai), analysis.LinkTier, decision.PolicyTier]));
    }

    public void Reset() => store.Reset();
}
