using Backstop.Models;

namespace Backstop.Services.Api;

// The site is public and one household is shared, so nothing may grow without end between disposals.
// Past these sizes the oldest go first, finished items before held ones. An open safeguarding alert is never dropped.
public sealed partial class MockBackstopApi
{
    internal const int MaxStoredChecks = 300, MaxStoredGuides = 200, MaxStoredExplanations = 200, MaxStoredStories = 200,
        MaxStoredCards = 200, MaxActivityLines = 300, MaxHelpRequests = 100, MaxChatMessages = 1000;

    // Caller holds the lock.
    private void TrimVisitorStores(string keep)
    {
        TrimChecks(keep);
        TrimOldest(_guides, MaxStoredGuides, keep);
        TrimOldest(_explanations, MaxStoredExplanations, keep);
        TrimOldest(_stories, MaxStoredStories, keep);

        var cards = _memories.Where(c => _visitorItems.ContainsKey(c.Id)).ToList();
        foreach (var c in cards.Where(c => c.Id != keep).OrderBy(c => Order(c.Id)).Take(cards.Count - MaxStoredCards))
        {
            _memories.Remove(c);
            Forget(c.Id);
        }

        var asked = _help.Where(h => _visitorItems.ContainsKey(h.Id)).ToList();
        foreach (var h in asked.Where(h => h.Id != keep).OrderBy(h => h.Done ? 0 : 1).ThenBy(h => Order(h.Id)).Take(asked.Count - MaxHelpRequests))
        {
            _help.Remove(h);
            Forget(h.Id);
        }
    }

    private void TrimChecks(string keep)
    {
        var over = _checks.Count - MaxStoredChecks;
        if (over <= 0) return;
        var drop = _checks
            .Where(kv => kv.Key != keep && !(kv.Value.Result.Status == ReviewStatus.Held && IsSafeguarding(kv.Value)))
            .OrderBy(kv => kv.Value.Result.Status != ReviewStatus.Held ? 0 : IsEscalation(kv.Value) ? 2 : 1)
            .ThenBy(kv => Order(kv.Key))
            .Take(over)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var id in drop)
        {
            _checks.Remove(id);
            Forget(id);
        }
    }

    private void TrimOldest<T>(Dictionary<string, T> store, int max, string keep)
    {
        var over = store.Count - max;
        if (over <= 0) return;
        foreach (var id in store.Keys.Where(k => k != keep).OrderBy(Order).Take(over).ToList())
        {
            store.Remove(id);
            Forget(id);
        }
    }

    private long Order(string id) => _visitorItems.TryGetValue(id, out var item) ? item.Order : long.MinValue;

    private void Forget(string id)
    {
        _visitorItems.Remove(id);
        foreach (var key in _savedFrom.Where(kv => kv.Key == id || kv.Value == id).Select(kv => kv.Key).ToList()) _savedFrom.Remove(key);
    }

    // Seeded lines are dated before _seededAt and stay. Lines are added in time order, so the oldest new ones are first. Caller holds the lock.
    private void TrimActivity()
    {
        var over = _activity.Count - MaxActivityLines;
        if (over <= 0) return;
        var first = _activity.FindIndex(a => a.At >= _seededAt);
        if (first >= 0) _activity.RemoveRange(first, Math.Min(over, _activity.Count - first));
    }

    // Seeded messages stay. Finished ones go before held ones, and a held one about someone at risk is never dropped. Caller holds the lock.
    private void TrimChat()
    {
        var over = _chat.Count - MaxChatMessages;
        if (over <= 0) return;
        var drop = _chat
            .Where(m => m.At >= _seededAt && !(m.State == ChatState.Held && m.Flags.Any(f => f.Id == "at-risk")))
            .OrderBy(m => m.State == ChatState.Held ? 1 : 0)
            .ThenBy(m => m.At)
            .Take(over)
            .ToHashSet();
        _chat.RemoveAll(drop.Contains);
    }

    // ---------------- Reset ----------------

    // Work that was waiting on the AI when the demo was reset must not write into the fresh household. Read before the wait, compared at the write.
    private int _generation;

    private int CurrentGeneration
    {
        get { lock (_gate) return _generation; }
    }

    // ---------------- Change events ----------------

    // One broken listener (a closed page) must not undo a change that is already saved, or stop the others hearing about it.
    // Never called while holding _gate: a listener may run for a while.
    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList().Cast<Action>())
        {
            try { h(); } catch (Exception) { /* a listener failed; the data is already saved */ }
        }
    }
}
