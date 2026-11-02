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

    public HelperPermissions Access => store.HelperAccess;
    public void SetAccess(HelperPermissions access) => store.SetHelperAccess(access);
    public bool Allows(HelperCapability capability) => Access.Allows(capability);
    public IReadOnlyList<ActivityEntry> GetMailActivity() => store.Activity();

    public IReadOnlyList<MailMessage> GetQueue() =>
        store.Query(m => m.State == MessageState.Held);

    public IReadOnlyList<MailMessage> GetDecided() =>
        store.Query(m => m.State == MessageState.Blocked || m.HelperDecision is not null).Take(6).ToList();

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
