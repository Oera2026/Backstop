using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api;

namespace Backstop.Services;

/// <summary>MOCK user-side service with live Meta AI Dev API checking where available.</summary>
public sealed partial class MockMailService(MailStore store, IMetaScamChecker? metaChecker = null) : IMailService
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
    [GeneratedRegex(@"\b(?:sort code|account number|account no|iban|bank details)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BankWords();
    [GeneratedRegex(@"\b(\d{2})-(\d{2})-(\d{2})\b")]
    private static partial Regex SixDigitsDashed();
    [GeneratedRegex(@"\b\d{8}\b")]
    private static partial Regex AccountNumber();
    // "pin" alone is an everyday word (a safety pin), so it only counts beside a number or as "my/your/card/bank PIN".
    [GeneratedRegex(@"\b(?:password|passcode|security code|cvv|cvc)\b|\b(?:my|your|card|bank) pin\b|\bpin (?:number|code)\b|\bpin\W{0,4}(?:is\W{0,4})?\d{4,6}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    // MOCK: replace with the real outgoing data-loss check.
    public OutgoingCheck CheckOutgoing(string to, string subject, string body)
    {
        var text = $"{subject}\n{body}";
        var reasons = new List<string>();
        if (HasCardNumber(text)) reasons.Add("It looks like a card number.");
        if (HasBankCode(text)) reasons.Add("It looks like a bank code.");
        if (Secret().IsMatch(text)) reasons.Add("It looks like a password or secret code.");
        return new OutgoingCheck(reasons.Count > 0, reasons);
    }

    // Long numbers are only card numbers when they pass the card checksum (Luhn), so phone and order numbers are left alone.
    private static bool HasCardNumber(string text) =>
        CardNumber().Matches(text).Any(m => PassesLuhn(new string(m.Value.Where(char.IsDigit).ToArray())));

    private static bool PassesLuhn(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1) d = d * 2 > 9 ? d * 2 - 9 : d * 2;
            sum += d;
        }
        return sum % 10 == 0;
    }

    // 12-34-56 is a sort code, but 27-10-26 is a date. A dashed number that reads as a date only counts
    // when an account number or bank words are there too.
    private static bool HasBankCode(string text)
    {
        if (BankWords().IsMatch(text)) return true;
        var hasAccount = AccountNumber().IsMatch(text);
        return SixDigitsDashed().Matches(text).Any(m => hasAccount || !ReadsAsDate(m));
    }

    private static bool ReadsAsDate(Match m)
    {
        int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), c = int.Parse(m.Groups[3].Value);
        static bool Day(int n) => n is >= 1 and <= 31;
        static bool Month(int n) => n is >= 1 and <= 12;
        return (Day(a) && Month(b))   // 27-10-26 (day first)
            || (Month(a) && Day(b))   // 10-27-26 (month first)
            || (Month(b) && Day(c));  // 26-10-27 (year first)
    }

    public SendResult Send(string to, string subject, string body)
    {
        to = to.Trim();

        // Fail closed: never write to an address nobody reads, whatever the UI did first.
        if (NoReply.IsNoReply(to))
            return new SendResult(null, false, "That is a no-reply address. Nobody reads messages sent to it.");

        var isNew = !store.IsKnownRecipient(to);
        var contact = store.Contacts().FirstOrDefault(c => c.Address.Equals(to, StringComparison.OrdinalIgnoreCase));
        var sent = new SentMessage
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ToName = contact?.Name ?? "",
            ToAddress = to,
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Body = body,
        };
        var notice = isNew
            ? new HelperNotice { Id = Guid.NewGuid().ToString("N")[..8], Address = to, Subject = sent.Subject }
            : null;

        // MOCK: the message is only stored in memory. Nothing is sent anywhere.
        store.AddSent(sent, notice);
        return new SendResult(sent, isNew, null);
    }

    public IReadOnlyList<Contact> GetContacts() => store.Contacts().Where(c => !NoReply.IsNoReply(c.Address)).ToList();
    public Contact? GetContact(string id) => store.GetContact(id);
    public Contact? FindContact(string address) => store.FindContact(address);
    public ContactPhoto? GetContactPhoto(string id) => store.Photo(id);
    public void RemoveContactPhoto(string id) => store.SetPhoto(id, null);

    public bool SetContactPhoto(string id, byte[] bytes)
    {
        if (store.GetContact(id) is null) return false;
        // Trust the bytes, never the file name or the browser's claim. SVG is refused on purpose (it can carry script).
        var type = Sniff(bytes);
        if (type is null) return false;
        store.SetPhoto(id, new ContactPhoto(bytes, type));
        return true;
    }

    private static string? Sniff(byte[] b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "image/gif";
        if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    public IReadOnlyList<SentMessage> GetSent() => store.Sent();
    public SentMessage? GetSent(string id) => store.FindSent(id);

    // Only Cleared mail is searched (GetInbox), so held or blocked mail can never turn up in a search.
    public SearchResults Search(string query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0) return new SearchResults([], [], []);
        bool Has(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        return new SearchResults(
            GetInbox().Where(m => Has(m.FromName) || Has(m.Subject) || Has(m.Body)).ToList(),
            store.Sent().Where(s => Has(s.ToName) || Has(s.ToAddress) || Has(s.Subject) || Has(s.Body)).ToList(),
            GetContacts().Where(c => Has(c.Name) || Has(c.Address) || Has(c.Relationship)).ToList());
    }

    public IReadOnlyList<CalendarEvent> GetEvents() => store.Events();

    public void AddEvent(string title, DateOnly date, TimeOnly? time, string place) =>
        store.AddEvent(new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Title = title.Trim(),
            Date = date,
            Time = time,
            Place = place.Trim(),
        });

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

                var result = scenario.Tiers[i];

                // Tier 1 is "Small model" - inspect email body with Meta AI (text-only payload to save cost)
                if (i == 1 && metaChecker is not null)
                {
                    try
                    {
                        var aiResult = await metaChecker.CheckScamAsync($"{scenario.Subject}\n\n{scenario.Body}", null, null);
                        if (!aiResult.Success)
                        {
                            throw new InvalidOperationException("Meta AI check failed or timed out: " + (aiResult.FailureNote ?? "Unknown error"));
                        }

                        if (aiResult.Verdict == Verdict.DontReply)
                        {
                            result = new TierResult("Meta AI", 220, TierStatus.Flagged, aiResult.Reason);
                        }
                    }
                    catch (Exception) when (!simulateFailure)
                    {
                        // Any unexpected error or timeout triggers fail-closed
                        throw;
                    }
                }

                // MOCK: the "Small model" is the tier we pretend crashes. Real code: any exception or timeout.
                if (simulateFailure && i == 1)
                    throw new InvalidOperationException("Simulated checker failure");

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
