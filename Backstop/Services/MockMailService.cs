using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services;

/// <summary>MOCK user-side service. Every "check" below is hardcoded demo logic.</summary>
public sealed partial class MockMailService(MailStore store) : IMailService
{
    private const int StepDelayMs = 450; // only so the demo timeline is watchable

    public event Action? Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    public IReadOnlyList<Scenario> Scenarios => MockData.Scenarios;

    public IReadOnlyList<MailMessage> GetInbox() =>
        store.Query(m => m.State == MessageState.Cleared);

    // Failed, Received and Scanning are never shown, but they are not "held by a helper" yet either.
    public int HeldCount => store.Query(m => m.State == MessageState.Held).Count;

    public MailMessage? GetClearedMessage(string id)
    {
        var m = store.Find(id);
        return m is { State: MessageState.Cleared } ? m : null;
    }

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardNumber();
    [GeneratedRegex(@"\b\d{2}-\d{2}-\d{2}\b|\b(sort code|account number|iban)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BankCode();
    [GeneratedRegex(@"\b(password|passcode|pin|security code|cvv)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    // MOCK: replace with the real outgoing data-loss check.
    public OutgoingCheck CheckOutgoing(string to, string subject, string body)
    {
        var text = $"{subject}\n{body}";
        var reasons = new List<string>();
        if (CardNumber().IsMatch(text)) reasons.Add("It looks like a card number.");
        if (BankCode().IsMatch(text)) reasons.Add("It looks like a bank code.");
        if (Secret().IsMatch(text)) reasons.Add("It looks like a password or secret code.");
        return new OutgoingCheck(reasons.Count > 0, reasons);
    }

    public void Send(string to, string subject, string body)
    {
        // MOCK: nothing is sent anywhere.
    }

    public async Task<MailMessage?> ReceiveAsync(string scenarioId, bool simulateFailure, Action<int, TierResult>? onTier = null)
    {
        var scenario = MockData.Scenarios.FirstOrDefault(s => s.Id == scenarioId);
        if (scenario is null) return null;

        var msg = MockData.CreateMessage(scenario);
        store.Add(msg); // state Received: invisible to the user

        var done = new List<TierResult>();
        try
        {
            store.Update(() => msg.State = MessageState.Scanning);

            for (var i = 0; i < scenario.Tiers.Count; i++)
            {
                var name = scenario.Tiers[i].Name;
                onTier?.Invoke(i, new TierResult(name, 0, TierStatus.Running, "Checking…"));
                await Task.Delay(StepDelayMs);

                // MOCK: the "Small model" is the tier we pretend crashes. Real code: any exception or timeout.
                if (simulateFailure && i == 1)
                    throw new InvalidOperationException("Simulated checker failure");

                var result = scenario.Tiers[i];
                done.Add(result);
                onTier?.Invoke(i, result);
            }

            // Fail closed: only an explicit Cleared or Blocked outcome is honoured. Everything else is Held.
            var final = scenario.Outcome switch
            {
                MessageState.Cleared => MessageState.Cleared,
                MessageState.Blocked => MessageState.Blocked,
                _ => MessageState.Held,
            };
            store.Update(() =>
            {
                msg.Tiers = done;
                msg.State = final;
                msg.ReceivedAt = DateTimeOffset.Now;
            });
        }
        catch (Exception)
        {
            FailClosed(msg, scenario, done, onTier);
        }
        return msg;
    }

    /// <summary>Failed -> Held. An error never lets a message through.</summary>
    private void FailClosed(MailMessage msg, Scenario scenario, List<TierResult> done, Action<int, TierResult>? onTier)
    {
        var failedAt = done.Count;
        if (failedAt < scenario.Tiers.Count)
        {
            var failed = new TierResult(scenario.Tiers[failedAt].Name, 0, TierStatus.Failed, "The checker had a problem.");
            done.Add(failed);
            onTier?.Invoke(failedAt, failed);
        }

        for (var i = failedAt + 1; i < scenario.Tiers.Count; i++)
        {
            var isPolicy = i == scenario.Tiers.Count - 1;
            var r = isPolicy
                ? new TierResult(scenario.Tiers[i].Name, 2, TierStatus.Flagged, "Something went wrong, so a helper looks first.")
                : new TierResult(scenario.Tiers[i].Name, 0, TierStatus.Skipped, "Not run.");
            done.Add(r);
            onTier?.Invoke(i, r);
        }

        store.Update(() =>
        {
            msg.State = MessageState.Failed;
            msg.Tiers = done;
            msg.Flags = [new Flag("alert", "The checker had a problem", "We could not finish checking this message, so it is held until a person looks at it."), .. msg.Flags];
            msg.Summary = "The automatic check did not finish. Please look at this message with care.";
            msg.Confidence = Confidence.Low;
            msg.ReceivedAt = DateTimeOffset.Now;
            msg.State = MessageState.Held; // explicit Failed -> Held transition
        });
    }

    public void Reset() => store.Reset();
}
