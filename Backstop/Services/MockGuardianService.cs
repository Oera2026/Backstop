using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services;

/// <summary>MOCK helper-side service over the shared in-memory store.</summary>
public sealed class MockGuardianService(MailStore store) : IGuardianService
{
    public event Action? Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    // Clamped here, in the service: nothing above Standard can be read back or stored, whatever a page asks for.
    public HelperPermissions Access => HelperPermissions.Clamp(store.HelperAccess);
    public void SetAccess(HelperPermissions access) => store.SetHelperAccess(HelperPermissions.Clamp(access));
    public bool Allows(HelperCapability capability) => Access.Allows(capability);
    public IReadOnlyList<ActivityEntry> GetMailActivity() => store.Activity();

    public IReadOnlyList<MailMessage> GetQueue() =>
        store.Query(m => m.State == MessageState.Held);

    public IReadOnlyList<MailMessage> GetDecided(int take = 6) =>
        store.Query(m => m.State == MessageState.Blocked || m.HelperDecision is not null).Take(take).ToList();

    public IReadOnlyList<MailMessage> GetCleared() =>
        Allows(HelperCapability.SeeClearedMail) ? store.Query(m => m.State == MessageState.Cleared) : [];

    public void CalledBack(string id)
    {
        string? subject = null;
        if (store.TryChange(id, m => m.State == MessageState.Held && m.CallRequested, m => { m.CallRequested = false; subject = m.Subject; }))
            store.AddActivity("phone", $"You called {Names.User} about \"{subject}\".");
    }

    public IReadOnlyList<SentMessage> GetUserSent() => Allows(HelperCapability.SeeFullActivity) ? store.Sent() : [];
    public IReadOnlyList<Contact> GetUserContacts() => Allows(HelperCapability.SeeFullActivity) ? store.Contacts() : [];
    public IReadOnlyList<CalendarEvent> GetUserEvents() => Allows(HelperCapability.SeeFullActivity) ? store.Events() : [];
    public IReadOnlyList<HelperNotice> GetSeenNotices() => Allows(HelperCapability.SeeFullActivity) ? store.SeenNotices() : [];

    public MailMessage? Get(string id)
    {
        var m = store.Find(id);
        if (m is null) return null;
        return m.State switch
        {
            MessageState.Held or MessageState.Blocked => m,
            // Cleared mail is the user's own. The helper sees it only if they decided it or were given that access.
            MessageState.Cleared when m.HelperDecision is not null || Allows(HelperCapability.SeeClearedMail) => m,
            // Half-scanned and unknown states are never shown.
            _ => null,
        };
    }

    public IReadOnlyList<HelperNotice> GetNotices() => store.OpenNotices();
    public void DismissNotice(string id) => store.DismissNotice(id);

    public IReadOnlyList<HeldOutgoing> GetHeldOutgoing() => store.OpenHeldOutgoing();
    public void ApproveOutgoing(string id) => DecideOutgoing(id, release: true);
    public void BlockOutgoing(string id) => DecideOutgoing(id, release: false);

    private void DecideOutgoing(string id, bool release)
    {
        var held = store.OpenHeldOutgoing().FirstOrDefault(h => h.Id == id);
        if (!store.DecideOutgoing(id, release) || held is null) return;
        store.AddActivity(release ? "send" : "cross",
            release ? $"You sent {Names.User}'s message \"{held.Subject}\"." : $"You stopped {Names.User}'s message \"{held.Subject}\".");
    }

    // Only a Held message can be decided. Blocked is final. Mail the AI has not checked yet cannot be approved.
    public void Approve(string id) => _ = Decide(id, MessageState.Cleared, "Approved", "check", "approved");
    public void Block(string id) => _ = Decide(id, MessageState.Blocked, "Blocked", "cross", "blocked");

    // ---- One-tap decisions with a 10 second undo ----
    // The message stays Held, and invisible to the user, until the window ends. Only then is the decision saved.
    // That keeps "Blocked is final" and fail closed: an undo never has to bring back mail anyone has seen.

    public const int DecisionSeconds = 10;
    public int DecisionWindowSeconds => DecisionSeconds;

    private readonly object _pendingGate = new();
    private readonly Dictionary<string, (bool Safe, DateTimeOffset At)> _pending = [];
    private readonly Dictionary<string, string> _failures = [];
    private Timer? _timer;

    /// <summary>The user's saved phone number. No number is collected or stored anywhere in the app yet, so this stays null.</summary>
    public string? UserPhone { get; set; }

    // Seams for tests: the clock, the timer, and a failure while saving.
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;
    internal bool AutoCommit { get; set; } = true;
    internal Action? SaveCheck { get; set; }

    public DecisionResult Propose(string id, bool safe)
    {
        var m = store.Find(id);
        if (m is null || m.State != MessageState.Held) return new(false, "This message is not waiting for you any more.");
        if (safe && m.AwaitingScan) return new(false, "The checker has not finished with this one. Please check it again first.");
        // Marked on the message itself, so a rescan cannot release it while the helper's decision waits out its window.
        if (!store.TryChange(id, x => x.State == MessageState.Held && !(safe && x.AwaitingScan), x => x.DecisionPending = true))
            return new(false, "This message is not waiting for you any more.");
        lock (_pendingGate)
        {
            _pending[id] = (safe, Now());
            _failures.Remove(id);
            if (AutoCommit)
            {
                _timer ??= new Timer(_ => CommitDue());
                _timer.Change(500, 500);
            }
        }
        return new(true);
    }

    public bool Undo(string id)
    {
        lock (_pendingGate)
        {
            if (!_pending.TryGetValue(id, out var p) || Now() - p.At >= TimeSpan.FromSeconds(DecisionSeconds)) return false;
            _pending.Remove(id);
        }
        store.TryChange(id, _ => true, x => x.DecisionPending = false);
        return true;
    }

    public PendingDecision? GetPending(string id)
    {
        lock (_pendingGate)
        {
            if (!_pending.TryGetValue(id, out var p)) return null;
            var left = (int)Math.Ceiling(DecisionSeconds - (Now() - p.At).TotalSeconds);
            return new(p.Safe, Math.Clamp(left, 0, DecisionSeconds));
        }
    }

    public string? TakeFailure(string id)
    {
        lock (_pendingGate) return _failures.Remove(id, out var why) ? why : null;
    }

    /// <summary>Saves every decision whose window has passed. Runs on a timer. A decision that cannot be saved leaves the message held.</summary>
    public void CommitDue()
    {
        List<(string Id, bool Safe)> due;
        lock (_pendingGate)
        {
            due = _pending.Where(p => Now() - p.Value.At >= TimeSpan.FromSeconds(DecisionSeconds)).Select(p => (p.Key, p.Value.Safe)).ToList();
            foreach (var d in due) _pending.Remove(d.Id);
            if (_pending.Count == 0) _timer?.Change(Timeout.Infinite, Timeout.Infinite); // nothing waiting: the timer sleeps until the next decision
        }
        foreach (var (id, safe) in due)
        {
            var target = safe ? MessageState.Cleared : MessageState.Blocked;
            try
            {
                var saved = safe ? Decide(id, MessageState.Cleared, "Approved", "check", "approved") : Decide(id, MessageState.Blocked, "Blocked", "cross", "blocked");
                // Not saved and not already where the helper wanted it: say so. A decision is never dropped without a word.
                if (!saved && store.Find(id)?.State != target) Fail(id);
            }
            catch (Exception) { Fail(id); } // fail closed: the message was not changed and stays held
            finally
            {
                try { store.TryChange(id, m => m.DecisionPending, m => m.DecisionPending = false); }
                catch (Exception) { /* the flag only pauses rescans; a reset or restart clears it */ }
            }
        }
    }

    private void Fail(string id)
    {
        var why = store.Find(id)?.State is null or MessageState.Held
            ? "We could not save that. The message is still held, so please try again."
            : "This message was decided in another window before your choice was saved.";
        lock (_pendingGate)
        {
            if (_failures.Count >= 200) _failures.Clear(); // nobody read them; keep the table small
            _failures[id] = why;
        }
    }

    public string? GetRingHref()
    {
        var digits = new string((UserPhone ?? "").Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        return digits.Count(char.IsAsciiDigit) >= 7 && digits.LastIndexOf('+') <= 0 ? "tel:" + digits : null;
    }

    public void CallFirst(string id)
    {
        string? subject = null;
        if (store.TryChange(id, m => m.State == MessageState.Held, m => { m.CallRequested = true; subject = m.Subject; }))
            store.AddActivity("phone", $"You will call {Names.User} about \"{subject}\".");
    }

    // The Held check and the change are one step, so a second decision on the same message does nothing.
    private bool Decide(string id, MessageState to, string decision, string icon, string verb)
    {
        string? subject = null;
        var changed = store.TryChange(id,
            m => m.State == MessageState.Held && !(to == MessageState.Cleared && m.AwaitingScan),
            m =>
            {
                SaveCheck?.Invoke(); // a failure here leaves the message untouched, still held
                m.State = to;
                m.HelperDecision = decision;
                m.CallRequested = false;
                m.DecisionPending = false;
                m.ReceivedAt = DateTimeOffset.Now; // lands at the top of the inbox when approved
                subject = m.Subject;
            });
        if (changed) store.AddActivity(icon, $"You {verb} \"{subject}\".");
        return changed;
    }

    private static class Names
    {
        public static string User => HouseholdFixture.Household.UserName;
    }
}
