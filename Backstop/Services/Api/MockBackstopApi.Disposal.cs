using Backstop.Models;

namespace Backstop.Services.Api;

// What a visitor sends to Meta AI through Check, Explain, Show me, Stories or Ask Me is kept only so they can read
// their answer, then it is deleted. The site is public and one household is shared, so nothing stays. Seeded data is left alone.
public sealed partial class MockBackstopApi
{
    /// <summary>How long a visitor's words, and anything made from them, stay in memory.</summary>
    internal static readonly TimeSpan VisitorDataLife = TimeSpan.FromMinutes(30);

    /// <summary>A seam for tests.</summary>
    internal Func<DateTimeOffset> DisposalClock { get; set; } = () => DateTimeOffset.Now;

    private readonly Dictionary<string, DateTimeOffset> _visitorItems = [];
    private DateTimeOffset _seededAt = DateTimeOffset.Now;

    // Caller holds the lock.
    private void MarkVisitorItem(string id) => _visitorItems[id] = DisposalClock();

    public int DisposeExpiredVisitorData()
    {
        int gone;
        lock (_gate)
        {
            var cutoff = DisposalClock() - VisitorDataLife;
            var expired = _visitorItems.Where(kv => kv.Value <= cutoff).Select(kv => kv.Key).ToList();
            foreach (var id in expired)
            {
                _visitorItems.Remove(id);
                DisposeItem(id);
            }
            // The activity log quotes titles and topics. Seeded lines are dated before _seededAt.
            var lines = _activity.RemoveAll(a => a.At >= _seededAt && a.At <= cutoff);
            gone = expired.Count + lines;
        }
        if (gone > 0) Changed?.Invoke();
        return gone;
    }

    // Caller holds the lock.
    private void DisposeItem(string id)
    {
        if (_checks.TryGetValue(id, out var c))
        {
            // Risk alerts are always on: an open safeguarding alert stays until the helper acts, without the person's words.
            if (c.Result.Status == ReviewStatus.Held && IsSafeguarding(c))
                _checks[id] = c with
                {
                    Original = "",
                    Result = c.Result with { Reason = "Someone may need help.", DisplayText = "", Highlights = [], Thread = null, WatchFor = null },
                };
            else _checks.Remove(id);
        }
        _guides.Remove(id);
        _explanations.Remove(id);
        _stories.Remove(id);
        _memories.RemoveAll(m => m.Id == id);
        _help.RemoveAll(h => h.Id == id);
        foreach (var key in _savedFrom.Where(kv => kv.Key == id || kv.Value == id).Select(kv => kv.Key).ToList()) _savedFrom.Remove(key);
    }
}
