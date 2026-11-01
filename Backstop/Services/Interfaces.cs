using Backstop.Models;

namespace Backstop.Services;

/// <summary>What the USER side can do. A real backend replaces the Mock implementation.</summary>
public interface IMailService
{
    /// <summary>Raised after any change to any message. Handlers may run on any thread.</summary>
    event Action? Changed;

    /// <summary>Cleared mail only. Held, blocked and unknown states are never returned.</summary>
    IReadOnlyList<MailMessage> GetInbox();

    /// <summary>How many messages the helper is checking. The user is told only this number.</summary>
    int HeldCount { get; }

    /// <summary>Returns the message only if it is Cleared. Fail closed: otherwise null.</summary>
    MailMessage? GetClearedMessage(string id);

    OutgoingCheck CheckOutgoing(string to, string subject, string body);

    IReadOnlyList<Contact> GetContacts();
    Contact? GetContact(string id);
    Contact? FindContact(string address);

    ContactPhoto? GetContactPhoto(string id);

    /// <summary>Saves a profile picture. Returns false (and saves nothing) unless the bytes really are a PNG, JPEG, WebP or GIF.</summary>
    bool SetContactPhoto(string id, byte[] bytes);
    void RemoveContactPhoto(string id);
    IReadOnlyList<SentMessage> GetSent();
    SentMessage? GetSent(string id);

    /// <summary>Searches Cleared mail, sent mail and contacts. Held and blocked mail is never searched.</summary>
    SearchResults Search(string query);

    IReadOnlyList<CalendarEvent> GetEvents();
    void AddEvent(string title, DateOnly date, TimeOnly? time, string place);

    /// <summary>MOCK: pretends to send. Nothing leaves the app. Refuses no-reply addresses and tells the helper about first-time recipients.</summary>
    SendResult Send(string to, string subject, string body);

    /// <summary>Keeps a private-looking message back for the helper. Nothing is sent until the helper approves. Returns false for a no-reply address.</summary>
    bool HoldOutgoing(string to, string subject, string body, IReadOnlyList<string> reasons);

    // --- Demo support ---
    IReadOnlyList<Scenario> Scenarios { get; }

    /// <summary>
    /// Runs the mock pipeline for a scenario. <paramref name="onTier"/> is called when a tier starts
    /// (status Running) and when it finishes. Any error ends in Failed -> Held.
    /// </summary>
    Task<MailMessage?> ReceiveAsync(string scenarioId, bool simulateFailure, Action<int, TierResult>? onTier = null);

    void Reset();
}

/// <summary>What the HELPER (guardian) side can do.</summary>
public interface IGuardianService
{
    event Action? Changed;

    IReadOnlyList<MailMessage> GetQueue();

    /// <summary>Messages already decided (approved, blocked by a helper, or auto-blocked).</summary>
    IReadOnlyList<MailMessage> GetDecided();

    MailMessage? Get(string id);

    void Approve(string id);
    void Block(string id);
    void CallFirst(string id);

    /// <summary>FYI notices the helper has not dismissed (for example: the user wrote to someone new).</summary>
    IReadOnlyList<HelperNotice> GetNotices();
    void DismissNotice(string id);

    /// <summary>The user's own messages waiting for the helper before they are sent.</summary>
    IReadOnlyList<HeldOutgoing> GetHeldOutgoing();
    void ApproveOutgoing(string id);
    void BlockOutgoing(string id);
}
