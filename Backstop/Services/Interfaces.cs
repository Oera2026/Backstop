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

    /// <summary>How many messages the helper blocked in the last day. Only a number: nothing about them is shown.</summary>
    int RecentHelperBlocks { get; }

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

    /// <summary>Made-up examples for the public Try it page.</summary>
    IReadOnlyList<TrySample> TrySamples { get; }

    /// <summary>
    /// The public Try it page: runs the same checks as real mail on one pasted email (incoming) or draft (outgoing) and says
    /// what Backstop would do. Keeps nothing and opens no link. No Meta answer is always Held, never Safe.
    /// </summary>
    Task<TryResult> TryCheckAsync(InboundMail mail, bool outgoing, CancellationToken ct = default);

    /// <summary>The Try it page's standalone link box. Same checks as a message that holds only that link.</summary>
    Task<TryResult> TryCheckLinkAsync(string url, string? shownText, CancellationToken ct = default);

    /// <summary>
    /// Runs the checking pipeline for a demo scenario. <paramref name="onTier"/> is called when a tier starts
    /// (status Running) and when it finishes. Any error ends in Failed -> Held.
    /// </summary>
    Task<MailMessage?> ReceiveAsync(string scenarioId, bool simulateFailure, Action<int, TierResult>? onTier = null);

    /// <summary>Same pipeline for any email, not only the scenarios. The outcome comes from the email's own text.</summary>
    Task<MailMessage?> ReceiveAsync(InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier = null);

    /// <summary>
    /// Same pipeline as ReceiveAsync for one visitor-typed email, but keeps nothing: the message is never added to
    /// the shared store and no other visitor sees it. The tier callbacks still fire so the demo timeline works.
    /// Any error ends Held, never Cleared.
    /// </summary>
    Task<MailMessage?> ReceiveStatelessAsync(InboundMail mail, bool simulateFailure, Action<int, TierResult>? onTier = null);

    /// <summary>
    /// Scans held mail that is waiting for the AI again (all of it, or just <paramref name="id"/>). Mail leaves
    /// the held state only when the AI answers. Returns how many got an answer. A failure leaves the mail held.
    /// </summary>
    Task<int> RescanAsync(string? id = null);

    void Reset();
}

/// <summary>What the HELPER (guardian) side can do.</summary>
public interface IGuardianService
{
    event Action? Changed;

    /// <summary>What the user allowed this helper to do. The real gate for every capability.</summary>
    HelperPermissions Access { get; }
    void SetAccess(HelperPermissions access);
    bool Allows(HelperCapability capability);

    /// <summary>What the helper did with mail (approved, blocked, called, outgoing decisions).</summary>
    IReadOnlyList<ActivityEntry> GetMailActivity();

    IReadOnlyList<MailMessage> GetQueue();

    /// <summary>Messages already decided (approved, blocked by a helper, or auto-blocked).</summary>
    IReadOnlyList<MailMessage> GetDecided(int take = 6);

    /// <summary>Mail the user can read. Empty unless the helper may see cleared mail.</summary>
    IReadOnlyList<MailMessage> GetCleared();

    /// <summary>The helper said they would call. Done calling: the message stays held until they decide.</summary>
    void CalledBack(string id);

    /// <summary>Everything below is empty (or refused) unless the helper was given that access.</summary>
    IReadOnlyList<SentMessage> GetUserSent();
    IReadOnlyList<Contact> GetUserContacts();
    IReadOnlyList<CalendarEvent> GetUserEvents();
    IReadOnlyList<HelperNotice> GetSeenNotices();

    MailMessage? Get(string id);

    void Approve(string id);
    void Block(string id);
    void CallFirst(string id);

    /// <summary>Takes back "Call first" while the message is still held.</summary>
    void CancelCall(string id);

    /// <summary>How long a one-tap decision can be undone, in seconds. Nothing changes for the user until it has passed.</summary>
    int DecisionWindowSeconds { get; }

    /// <summary>
    /// One tap: "Safe, let it through" (<paramref name="safe"/>) or "Not safe, block it". The message stays held and unseen
    /// for the undo window, then the decision is saved. Refuses (and starts nothing) for mail that is not held or not checked yet.
    /// </summary>
    DecisionResult Propose(string id, bool safe);

    /// <summary>Takes back a decision still inside its window. False when there is none, or the window has passed.</summary>
    bool Undo(string id);

    /// <summary>The decision waiting out its window for this message, or null.</summary>
    PendingDecision? GetPending(string id);

    /// <summary>Plain words when the last decision on this message could not be saved (it stays held). Returned once.</summary>
    string? TakeFailure(string id);

    /// <summary>A tel: link to the user's saved phone number, or null when there is none (the Ring them button is then hidden).</summary>
    string? GetRingHref();

    /// <summary>FYI notices the helper has not dismissed (for example: the user wrote to someone new).</summary>
    IReadOnlyList<HelperNotice> GetNotices();
    void DismissNotice(string id);

    /// <summary>The user's own messages waiting for the helper before they are sent.</summary>
    IReadOnlyList<HeldOutgoing> GetHeldOutgoing();
    void ApproveOutgoing(string id);
    void BlockOutgoing(string id);
}
