using Backstop.Models;

namespace Backstop.Services;

/// <summary>MOCK helper-side service over the shared in-memory store.</summary>
public sealed class MockGuardianService(MailStore store) : IGuardianService
{
    public event Action? Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    public IReadOnlyList<MailMessage> GetQueue() =>
        store.Query(m => m.State == MessageState.Held);

    public IReadOnlyList<MailMessage> GetDecided() =>
        store.Query(m => m.State == MessageState.Blocked || m.HelperDecision is not null).Take(6).ToList();

    public MailMessage? Get(string id)
    {
        var m = store.Find(id);
        // The helper can open anything that finished checking, never a half-scanned message.
        return m is { State: MessageState.Held or MessageState.Blocked or MessageState.Cleared } ? m : null;
    }

    public IReadOnlyList<HelperNotice> GetNotices() => store.OpenNotices();
    public void DismissNotice(string id) => store.DismissNotice(id);

    // Only a Held message can be decided. Blocked is final.
    public void Approve(string id) => Decide(id, MessageState.Cleared, "Approved");
    public void Block(string id) => Decide(id, MessageState.Blocked, "Blocked");

    public void CallFirst(string id) =>
        store.TryChange(id, m => m.State == MessageState.Held, m => m.CallRequested = true);

    // The Held check and the change are one step, so a second decision on the same message does nothing.
    private void Decide(string id, MessageState to, string decision) =>
        store.TryChange(id, m => m.State == MessageState.Held, m =>
        {
            m.State = to;
            m.HelperDecision = decision;
            m.CallRequested = false;
            m.ReceivedAt = DateTimeOffset.Now; // lands at the top of the inbox when approved
        });
}
