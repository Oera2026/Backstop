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
    private List<Contact> _contacts = MockData.SeedContacts();
    private readonly Dictionary<string, ContactPhoto> _photos = [];
    private List<SentMessage> _sent = MockData.SeedSent();
    private List<CalendarEvent> _events = MockData.SeedEvents();
    private List<HelperNotice> _notices = [];

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

    /// <summary>
    /// Change one message only if it still passes <paramref name="allowed"/>. The check and the change happen
    /// under one lock, so two decisions at once cannot both pass. Returns false when nothing changed.
    /// </summary>
    public bool TryChange(string id, Func<MailMessage, bool> allowed, Action<MailMessage> change)
    {
        lock (_gate)
        {
            var m = _all.FirstOrDefault(x => x.Id == id);
            if (m is null || !allowed(m)) return false;
            change(m);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Apply a change under the lock, then tell everyone.</summary>
    public void Update(Action change)
    {
        lock (_gate) change();
        Changed?.Invoke();
    }

    // --- Contacts, sent mail, calendar, helper notices ---

    public IReadOnlyList<Contact> Contacts()
    {
        lock (_gate) return _contacts.OrderBy(c => c.Name).ToList();
    }

    public Contact? GetContact(string id)
    {
        lock (_gate) return _contacts.FirstOrDefault(c => c.Id == id);
    }

    public Contact? FindContact(string address)
    {
        var a = address.Trim();
        lock (_gate) return _contacts.FirstOrDefault(c => c.Address.Equals(a, StringComparison.OrdinalIgnoreCase));
    }

    public ContactPhoto? Photo(string id)
    {
        lock (_gate) return _photos.GetValueOrDefault(id);
    }

    /// <summary>Pass null to take the picture away.</summary>
    public void SetPhoto(string id, ContactPhoto? photo)
    {
        lock (_gate)
        {
            var i = _contacts.FindIndex(c => c.Id == id);
            if (i < 0) return;
            if (photo is null)
            {
                _photos.Remove(id);
                _contacts[i] = _contacts[i] with { PhotoVersion = null };
            }
            else
            {
                _photos[id] = photo;
                _contacts[i] = _contacts[i] with { PhotoVersion = Guid.NewGuid().ToString("N")[..8] };
            }
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<SentMessage> Sent()
    {
        lock (_gate) return _sent.OrderByDescending(s => s.SentAt).ToList();
    }

    public SentMessage? FindSent(string id)
    {
        lock (_gate) return _sent.FirstOrDefault(s => s.Id == id);
    }

    public IReadOnlyList<CalendarEvent> Events()
    {
        lock (_gate) return _events.OrderBy(e => e.Date).ThenBy(e => e.Time ?? TimeOnly.MinValue).ToList();
    }

    /// <summary>Notices the helper has not dismissed yet, newest first.</summary>
    public IReadOnlyList<HelperNotice> OpenNotices()
    {
        lock (_gate) return _notices.Where(n => !n.Seen).OrderByDescending(n => n.At).ToList();
    }

    /// <summary>True when the address is a contact or has been written to before.</summary>
    public bool IsKnownRecipient(string address)
    {
        var a = address.Trim();
        lock (_gate)
        {
            return _contacts.Any(c => c.Address.Equals(a, StringComparison.OrdinalIgnoreCase))
                || _sent.Any(s => s.ToAddress.Equals(a, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddSent(SentMessage message, HelperNotice? notice)
    {
        lock (_gate)
        {
            _sent.Add(message);
            if (notice is not null) _notices.Add(notice);
        }
        Changed?.Invoke();
    }

    public void AddEvent(CalendarEvent e)
    {
        lock (_gate) _events.Add(e);
        Changed?.Invoke();
    }

    public void DismissNotice(string id)
    {
        lock (_gate) _notices.FirstOrDefault(n => n.Id == id)?.Let(n => n.Seen = true);
        Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _all = MockData.Seed();
            _contacts = MockData.SeedContacts();
            _photos.Clear();
            _sent = MockData.SeedSent();
            _events = MockData.SeedEvents();
            _notices = [];
        }
        Changed?.Invoke();
    }
}

internal static class StoreExtensions
{
    public static void Let<T>(this T value, Action<T> action) => action(value);
}
