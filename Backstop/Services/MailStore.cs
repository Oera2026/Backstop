using Backstop.Models;

namespace Backstop.Services;

/// <summary>
/// Shared in-memory state (singleton) so the user and helper views, in different browser
/// sessions, see each other's changes live. MOCK: replaced by a database in a real backend.
/// </summary>
public sealed class MailStore
{
    private readonly object _gate = new();
    private List<MailMessage> _all = MockData.Seed();

    public event Action? Changed;

    public List<MailMessage> Query(Func<MailMessage, bool> where)
    {
        lock (_gate) return _all.Where(where).OrderByDescending(m => m.ReceivedAt).ToList();
    }

    public MailMessage? Find(string id)
    {
        lock (_gate) return _all.FirstOrDefault(m => m.Id == id);
    }

    public void Add(MailMessage message)
    {
        lock (_gate) _all.Add(message);
        Changed?.Invoke();
    }

    /// <summary>Apply a change under the lock, then tell everyone.</summary>
    public void Update(Action change)
    {
        lock (_gate) change();
        Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_gate) _all = MockData.Seed();
        Changed?.Invoke();
    }
}
