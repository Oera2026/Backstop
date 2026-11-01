using Microsoft.Extensions.Configuration;

namespace Backstop.Services.Api;

/// <summary>
/// A sliding one-minute cap on calls to Meta, shared by the checker and Ask Me. It stops a burst (or a loop) from
/// using up the Meta quota for everyone. Over the cap, the caller fails closed: held, never cleared.
/// In memory on purpose: this app runs as a single instance.
/// </summary>
public sealed class MetaCallGuard
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Queue<DateTime> _calls = new();
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;

    public int Limit { get; }

    public MetaCallGuard(IConfiguration config) : this(config.GetValue("MetaAI:MaxCallsPerMinute", 40)) { }

    internal MetaCallGuard(int limit, Func<DateTime>? now = null)
    {
        Limit = Math.Max(1, limit);
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>True if the call may go ahead (and is counted). Otherwise says how long to wait.</summary>
    public bool TryEnter(out int retryAfterSeconds, out int used)
    {
        lock (_gate)
        {
            var now = _now();
            while (_calls.Count > 0 && now - _calls.Peek() >= Window) _calls.Dequeue();
            used = _calls.Count;
            if (_calls.Count >= Limit)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((Window - (now - _calls.Peek())).TotalSeconds));
                return false;
            }
            _calls.Enqueue(now);
            retryAfterSeconds = 0;
            return true;
        }
    }
}
