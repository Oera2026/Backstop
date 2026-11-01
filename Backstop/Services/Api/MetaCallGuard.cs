using Microsoft.Extensions.Configuration;

namespace Backstop.Services.Api;

/// <summary>
/// Caps on calls to Meta, shared by the checker, Ask Me and Explain. Three limits, checked together so a refused
/// call is never counted: a per-visitor cap per minute (so one visitor cannot use everyone's share), a cap for
/// the whole app per minute, and a cap for the whole app per day (so a bill cannot run away).
/// Over any cap, the caller fails closed: held, never cleared.
/// In memory on purpose: this app runs as a single instance. A restart (or a sleeping free host) resets the counts,
/// so the daily cap is a soft guard, not an accounting figure.
/// </summary>
public sealed class MetaCallGuard
{
    public enum Denial { None, PerMinute, PerVisitor, PerDay }

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxTrackedVisitors = 5000;
    private readonly Queue<DateTime> _calls = new();
    private readonly Dictionary<string, Queue<DateTime>> _byVisitor = [];
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;
    private DateOnly _day;
    private int _dayCount;

    /// <summary>Calls per minute for the whole app.</summary>
    public int Limit { get; }
    /// <summary>Calls per minute for one visitor.</summary>
    public int VisitorLimit { get; }
    /// <summary>Calls per UTC day for the whole app.</summary>
    public int DayLimit { get; }

    public MetaCallGuard(IConfiguration config) : this(
        config.GetValue("MetaAI:MaxCallsPerMinute", 40),
        null, Read(config, "META_AI_MAX_CALLS_PER_IP_PER_MINUTE", "MetaAI:MaxCallsPerIpPerMinute", 15),
        Read(config, "META_AI_MAX_CALLS_PER_DAY", "MetaAI:MaxCallsPerDay", 1500)) { }

    internal MetaCallGuard(int limit, Func<DateTime>? now = null, int visitorLimit = int.MaxValue, int dayLimit = int.MaxValue)
    {
        Limit = Math.Max(1, limit);
        VisitorLimit = Math.Max(1, visitorLimit);
        DayLimit = Math.Max(1, dayLimit);
        _now = now ?? (() => DateTime.UtcNow);
    }

    private static int Read(IConfiguration config, string envName, string key, int fallback) =>
        int.TryParse(config[envName], out var a) && a > 0 ? a
        : int.TryParse(config[key], out var b) && b > 0 ? b
        : fallback;

    /// <summary>True if the call may go ahead (and is counted). Otherwise says how long to wait. No visitor limit applies.</summary>
    public bool TryEnter(out int retryAfterSeconds, out int used) =>
        TryEnter(null, out retryAfterSeconds, out used, out _);

    /// <summary>
    /// As above, for a known visitor. <paramref name="visitor"/> is null when the address could not be worked out:
    /// then only the two app-wide caps apply.
    /// </summary>
    public bool TryEnter(string? visitor, out int retryAfterSeconds, out int used, out Denial denial)
    {
        lock (_gate)
        {
            var now = _now();
            Trim(_calls, now);

            // The visitor cap comes first, so one noisy visitor is refused before they touch the shared caps.
            Queue<DateTime>? mine = null;
            if (visitor is not null)
            {
                if (!_byVisitor.TryGetValue(visitor, out mine))
                {
                    if (_byVisitor.Count >= MaxTrackedVisitors) Sweep(now);
                    mine = _byVisitor[visitor] = new Queue<DateTime>();
                }
                Trim(mine, now);
                if (mine.Count >= VisitorLimit)
                {
                    used = mine.Count;
                    retryAfterSeconds = Wait(mine, now);
                    denial = Denial.PerVisitor;
                    return false;
                }
            }

            var today = DateOnly.FromDateTime(now);
            if (today != _day) { _day = today; _dayCount = 0; }
            if (_dayCount >= DayLimit)
            {
                used = _dayCount;
                retryAfterSeconds = (int)Math.Max(1, Math.Ceiling((today.AddDays(1).ToDateTime(TimeOnly.MinValue) - now).TotalSeconds));
                denial = Denial.PerDay;
                return false;
            }

            if (_calls.Count >= Limit)
            {
                used = _calls.Count;
                retryAfterSeconds = Wait(_calls, now);
                denial = Denial.PerMinute;
                return false;
            }

            _calls.Enqueue(now);
            mine?.Enqueue(now);
            _dayCount++;
            used = _calls.Count;
            retryAfterSeconds = 0;
            denial = Denial.None;
            return true;
        }
    }

    private static void Trim(Queue<DateTime> q, DateTime now)
    {
        while (q.Count > 0 && now - q.Peek() >= Window) q.Dequeue();
    }

    private static int Wait(Queue<DateTime> q, DateTime now) =>
        Math.Max(1, (int)Math.Ceiling((Window - (now - q.Peek())).TotalSeconds));

    /// <summary>Forgets visitors with no recent calls, so the table cannot grow without end.</summary>
    private void Sweep(DateTime now)
    {
        foreach (var key in _byVisitor.Keys.ToList())
        {
            Trim(_byVisitor[key], now);
            if (_byVisitor[key].Count == 0) _byVisitor.Remove(key);
        }
        // Still full of active visitors: drop the table rather than grow it. The app-wide caps still hold.
        if (_byVisitor.Count >= MaxTrackedVisitors) _byVisitor.Clear();
    }
}
