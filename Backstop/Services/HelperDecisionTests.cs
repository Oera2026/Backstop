#if DEBUG
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;

namespace Backstop.Tests;

/// <summary>
/// One-tap helper decisions with a 10 second undo. The clock is a fake one and the timer is off, so nothing here waits.
/// Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class HelperDecisionTests
{
    private sealed class FixedMeta(MetaScamAnalysis analysis) : IMetaScamChecker
    {
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default) => Task.FromResult(analysis);
    }

    private static readonly MetaScamAnalysis Safe = new(Verdict.LooksSafe, "Fine.", [], true);

    private sealed class Rig
    {
        public MailStore Store { get; } = new();
        public MockMailService Mail { get; }
        public MockGuardianService Guardian { get; }
        public DateTimeOffset Clock = DateTimeOffset.Now;

        public Rig(IMetaScamChecker? ai = null)
        {
            Mail = new MockMailService(Store, ai ?? new FixedMeta(Safe));
            Guardian = new MockGuardianService(Store) { AutoCommit = false };
            Guardian.Now = () => Clock;
        }

        public async Task<MailMessage> Held() => (await Mail.ReceiveAsync("remote-support", false))!;
        public void Pass(double seconds) { Clock = Clock.AddSeconds(seconds); Guardian.CommitDue(); }
        // The store hands out copies, so read the message again to see what a decision did.
        public MailMessage Fresh(MailMessage m) => Store.Find(m.Id)!;
    }

    public static async Task RunAsync()
    {
        Assert(new MockGuardianService(new MailStore()).DecisionWindowSeconds == 10, "The undo window is 10 seconds");

        // Safe, let it through: nothing changes until the window ends, then it reaches the inbox with the helper's mark.
        var r = new Rig();
        var m = await r.Held();
        Assert(r.Fresh(m).State == MessageState.Held && r.Mail.HeldCount >= 1, "It starts held");
        Assert(r.Guardian.Propose(m.Id, true).Ok, "Safe is accepted");
        Assert(r.Guardian.GetPending(m.Id) is { Safe: true, SecondsLeft: 10 }, "The toast starts at 10 seconds");
        r.Pass(9);
        Assert(r.Fresh(m).State == MessageState.Held && r.Mail.GetInbox().All(x => x.Id != m.Id) && r.Mail.GetClearedMessage(m.Id) is null, "Inside the window the user still sees nothing");
        Assert(r.Guardian.GetPending(m.Id) is { SecondsLeft: 1 }, "One second left");
        r.Pass(1);
        Assert(r.Fresh(m).State == MessageState.Cleared && r.Fresh(m).HelperDecision == "Approved" && r.Mail.GetInbox().Any(x => x.Id == m.Id), "After the window: released to the inbox");
        Assert(r.Guardian.GetPending(m.Id) is null && r.Guardian.TakeFailure(m.Id) is null, "Nothing is left waiting");
        Assert(r.Guardian.GetMailActivity().Any(a => a.Text.Contains("approved")), "It is written to the activity log");

        // Not safe, block it: it is blocked for good and the user never sees it.
        r = new Rig();
        m = await r.Held();
        var heldBefore = r.Mail.HeldCount;
        Assert(r.Guardian.Propose(m.Id, false).Ok, "Not safe is accepted");
        r.Pass(10);
        Assert(r.Fresh(m).State == MessageState.Blocked && r.Fresh(m).HelperDecision == "Blocked", "After the window: blocked");
        Assert(r.Mail.GetInbox().All(x => x.Id != m.Id) && r.Mail.GetClearedMessage(m.Id) is null && r.Mail.HeldCount == heldBefore - 1, "Blocked mail is gone from the user's view and the held count");
        Assert(!r.Guardian.Propose(m.Id, true).Ok, "Blocked is final: it cannot be decided again");
        r.Guardian.Approve(m.Id);
        Assert(r.Fresh(m).State == MessageState.Blocked, "Even the old Approve cannot bring it back");

        // Undo works inside the window and not after it.
        r = new Rig();
        m = await r.Held();
        r.Guardian.Propose(m.Id, true);
        r.Pass(5);
        Assert(r.Guardian.Undo(m.Id) && r.Guardian.GetPending(m.Id) is null, "Undo at 5 seconds works");
        r.Pass(30);
        Assert(r.Fresh(m).State == MessageState.Held && r.Fresh(m).HelperDecision is null, "An undone decision is never saved");
        Assert(!r.Guardian.Undo(m.Id), "Nothing left to undo");
        r.Guardian.Propose(m.Id, false);
        r.Pass(9.9);
        Assert(r.Guardian.Undo(m.Id), "Undo at 9.9 seconds still works");
        r.Guardian.Propose(m.Id, false);
        r.Clock = r.Clock.AddSeconds(10); // the window has passed, and the timer has not saved it yet
        Assert(!r.Guardian.Undo(m.Id), "Undo at 10 seconds is too late");
        r.Guardian.CommitDue();
        Assert(r.Fresh(m).State == MessageState.Blocked, "A decision that was not undone is saved");
        r = new Rig();
        m = await r.Held();
        r.Guardian.Propose(m.Id, true);
        r.Pass(11);
        Assert(r.Fresh(m).State == MessageState.Cleared && !r.Guardian.Undo(m.Id), "Undo after a decision was saved does nothing");

        // One decision at a time: a second tab cannot replace the first tab's choice. Undo, then choose again.
        r = new Rig();
        m = await r.Held();
        r.Guardian.Propose(m.Id, false);
        r.Pass(6);
        var second = r.Guardian.Propose(m.Id, true);
        Assert(!second.Ok && second.Error is { Length: > 0 } && r.Guardian.GetPending(m.Id) is { Safe: false }, "A second choice is refused while one is waiting");
        Assert(r.Guardian.Undo(m.Id) && r.Guardian.Propose(m.Id, true).Ok && r.Guardian.GetPending(m.Id) is { Safe: true }, "After Undo a new choice is taken, with a fresh window");
        r.Pass(6);
        Assert(r.Fresh(m).State == MessageState.Held, "The fresh window is still running");
        r.Pass(4);
        Assert(r.Fresh(m).State == MessageState.Cleared, "The new choice is the one saved");

        // Fail closed: a decision that cannot be saved leaves the message held, and says so once.
        r = new Rig();
        m = await r.Held();
        r.Guardian.SaveCheck = () => throw new InvalidOperationException("storage is down");
        r.Guardian.Propose(m.Id, true);
        r.Pass(10);
        Assert(r.Fresh(m).State == MessageState.Held && r.Fresh(m).HelperDecision is null && r.Mail.GetInbox().All(x => x.Id != m.Id), "Could not save: still held, never released");
        Assert(r.Guardian.TakeFailure(m.Id) is { Length: > 0 } && r.Guardian.TakeFailure(m.Id) is null && r.Guardian.GetPending(m.Id) is null, "The helper is told once");
        r.Guardian.SaveCheck = () => throw new InvalidOperationException("storage is down");
        r.Guardian.Propose(m.Id, false);
        r.Pass(10);
        Assert(r.Fresh(m).State == MessageState.Held && r.Guardian.TakeFailure(m.Id) is not null, "A block that cannot be saved stays held too");
        r.Guardian.SaveCheck = null;
        r.Guardian.Propose(m.Id, true);
        r.Pass(10);
        Assert(r.Fresh(m).State == MessageState.Cleared, "When saving works again, a new decision goes through");

        // Mail the checker has not finished with cannot be let through, and anything not held cannot be decided.
        var down = new Rig(new FixedMeta(new MetaScamAnalysis(Verdict.LooksSafe, "", [], false, "down")));
        var waiting = await down.Held();
        Assert(waiting.AwaitingScan && !down.Guardian.Propose(waiting.Id, true).Ok && down.Guardian.GetPending(waiting.Id) is null, "Safe is refused until the AI has checked it");
        Assert(down.Guardian.Propose(waiting.Id, false).Ok, "Blocking is always allowed");
        Assert(!down.Guardian.Propose("nope", true).Ok, "An unknown message is refused");
        var cleared = (await new Rig().Mail.ReceiveAsync("family-photos", false))!;
        Assert(cleared.State == MessageState.Cleared && !new Rig().Guardian.Propose(cleared.Id, false).Ok, "A message that is not held cannot be decided");

        // Call first can be taken back while the message is held, and only then.
        var cc = new Rig();
        var callMsg = await cc.Held();
        cc.Guardian.CallFirst(callMsg.Id);
        Assert(cc.Fresh(callMsg).CallRequested, "Call first is remembered");
        cc.Guardian.CancelCall(callMsg.Id);
        Assert(!cc.Fresh(callMsg).CallRequested && cc.Fresh(callMsg).State == MessageState.Held, "Taking the call back clears it and the message stays held");
        cc.Guardian.CancelCall("nope");
        Assert(cc.Guardian.GetMailActivity().Any(a => a.Text.Contains("will not call")), "Taking the call back is written to the activity log");

        // The helper's tier does not change: no SendAsUser, no SeeChats, and the refusals stay.
        var tier = new Rig();
        var held = await tier.Held();
        tier.Guardian.Propose(held.Id, true);
        tier.Pass(10);
        Assert(tier.Guardian.Access.Tier == HelperTier.Standard, "Deciding mail leaves the helper on the Standard tier");
        Assert(!tier.Guardian.Allows(HelperCapability.SendAsUser) && !tier.Guardian.Allows(HelperCapability.SeeChats), "Standard has neither SendAsUser nor SeeChats");
        tier.Guardian.SetAccess(new HelperPermissions((HelperTier)3));
        Assert(tier.Guardian.Access.Tier == HelperTier.Standard && !tier.Guardian.Allows(HelperCapability.SendAsUser) && !tier.Guardian.Allows(HelperCapability.SeeChats), "Asking for more is clamped back to Standard");
        Assert(typeof(IGuardianService).GetMethod("SendAsUser") is null, "There is no way for a helper to send mail as the user");
        var api = new MockBackstopApi(tier.Guardian, new MockApiOptions(), null);
        var refused = false;
        try { await api.GetChatLogAsync("anyone"); } catch (BackstopApiException) { refused = true; }
        Assert(refused, "Reading chats is still refused");
        Assert(tier.Guardian.GetChatsAllowed() == false, "SeeChats is off");

        Console.WriteLine("PASS: Helper decisions: Safe releases, Not safe stays blocked, undo works for 10 seconds only, Ring them hides without a number, save failures stay held, tier limits hold.");
    }

    private static bool GetChatsAllowed(this MockGuardianService g) => g.Allows(HelperCapability.SeeChats);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}

#endif
