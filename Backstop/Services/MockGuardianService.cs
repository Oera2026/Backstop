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

    public SendResult SendAsUser(string to, string subject, string body)
    {
        if (!Allows(HelperCapability.SendAsUser)) return new(null, false, $"{Names.User} has not allowed you to send mail as them.");
        to = (to ?? "").Trim();
        if (!InputGuard.IsPlainAddress(to)) return new(null, false, "Please check the address.");
        if (!InputGuard.IsSingleLine(subject ?? "", 200)) return new(null, false, "The subject is not right. Please shorten it.");
        if (string.IsNullOrWhiteSpace(body) || body.Length > 20000) return new(null, false, "Please write the message, and keep it under 20,000 letters.");
        if (NoReply.IsNoReply(to)) return new(null, false, "Nobody reads replies to that address.");

        var isNew = !store.IsKnownRecipient(to);
        var contact = store.FindContact(to);
        var sent = new SentMessage
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ToName = contact?.Name ?? "",
            ToAddress = to,
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Body = body,
            SentBy = HouseholdFixture.Household.TrustedPersonName,
        };
        // MOCK: the message is only stored in memory. Nothing is sent anywhere.
        store.AddSent(sent, null);
        store.AddActivity("send", $"You sent an email to {to} as {Names.User}: \"{sent.Subject}\".");
        return new(sent, isNew, null);
    }

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
    public void Approve(string id) => Decide(id, MessageState.Cleared, "Approved", "check", "approved");
    public void Block(string id) => Decide(id, MessageState.Blocked, "Blocked", "cross", "blocked");

    public void CallFirst(string id)
    {
        string? subject = null;
        if (store.TryChange(id, m => m.State == MessageState.Held, m => { m.CallRequested = true; subject = m.Subject; }))
            store.AddActivity("phone", $"You will call {Names.User} about \"{subject}\".");
    }

    // The Held check and the change are one step, so a second decision on the same message does nothing.
    private void Decide(string id, MessageState to, string decision, string icon, string verb)
    {
        string? subject = null;
        var changed = store.TryChange(id,
            m => m.State == MessageState.Held && !(to == MessageState.Cleared && m.AwaitingScan),
            m =>
            {
                m.State = to;
                m.HelperDecision = decision;
                m.CallRequested = false;
                m.ReceivedAt = DateTimeOffset.Now; // lands at the top of the inbox when approved
                subject = m.Subject;
            });
        if (changed) store.AddActivity(icon, $"You {verb} \"{subject}\".");
    }

    private static class Names
    {
        public static string User => HouseholdFixture.Household.UserName;
    }
}
