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

    // Only a Held message can be decided. Blocked is final.
    public void Approve(string id) => Decide(id, MessageState.Cleared, "Approved");
    public void Block(string id) => Decide(id, MessageState.Blocked, "Blocked");

    public void CallFirst(string id)
    {
        var m = store.Find(id);
        if (m is { State: MessageState.Held }) store.Update(() => m.CallRequested = true);
    }

    private void Decide(string id, MessageState to, string decision)
    {
        var m = store.Find(id);
        if (m is not { State: MessageState.Held }) return;
        store.Update(() =>
        {
            m.State = to;
            m.HelperDecision = decision;
            m.CallRequested = false;
            m.ReceivedAt = DateTimeOffset.Now; // lands at the top of the inbox when approved
        });
    }
}
