using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

public sealed partial class MockBackstopApi
{
    private const int MaxChatLength = 500;

    private sealed class StoredChat(string id, string personId, bool fromMe, string text, DateTimeOffset at, ChatState state)
    {
        public string Id { get; } = id;
        public string PersonId { get; } = personId;
        public bool FromMe { get; } = fromMe;
        public string Text { get; } = text;
        public DateTimeOffset At { get; set; } = at;
        public ChatState State { get; set; } = state;
        public Verdict Verdict { get; set; } = Verdict.LooksSafe;
        public IReadOnlyList<RiskFlag> Flags { get; set; } = [];
        public string Reason { get; set; } = "";
    }

    private List<StoredChat> _chat = SeedChat();
    private readonly Dictionary<string, int> _chatSent = []; // how many messages the user has sent each person

    private static List<StoredChat> SeedChat()
    {
        var now = DateTimeOffset.Now;
        StoredChat M(string person, bool fromMe, string text, int minutesAgo) =>
            new(NewId("c"), person, fromMe, text, now.AddMinutes(-minutesAgo), ChatState.Delivered);
        return
        [
            M(ChatFixtures.TrustedId, false, "Morning Mum. I will ring you at six tonight.", 300),
            M(ChatFixtures.TrustedId, true, "Thank you love. I will be in.", 290),
            M("p-arthur", false, "Hello Margaret! Did you enjoy the quiz last week?", 1500),
            M("p-arthur", true, "I did. Your team won!", 1490),
        ];
    }

    // Caller holds the lock.
    private void ResetChat()
    {
        _chat = SeedChat();
        _chatSent.Clear();
        guardian.SetAccess(HelperPermissions.Default);
    }

    public IReadOnlyList<ChatPerson> ChatPeople => ChatFixtures.People;

    public HelperPermissions HelperAccess => guardian.Access;

    public void SetHelperAccess(HelperPermissions access)
    {
        guardian.SetAccess(access);
        Changed?.Invoke();
    }

    // ---------------- User side ----------------

    public async Task<IReadOnlyList<ChatThread>> GetChatThreadsAsync(CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate)
            return ChatFixtures.People
                .Select(p =>
                {
                    var last = _chat.Where(m => m.PersonId == p.Id && m.State == ChatState.Delivered).MaxBy(m => m.At);
                    return new ChatThread(p, last?.Text, last?.At);
                })
                .OrderByDescending(t => t.LastAt ?? DateTimeOffset.MinValue)
                .ToList();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetChatMessagesAsync(string personId, CancellationToken ct = default)
    {
        await Wait(300, ct);
        if (ChatFixtures.Find(personId) is null) throw new BackstopApiException("You can only message people on your list.");
        lock (_gate) return Delivered(personId);
    }

    public async Task<int> GetHeldChatCountAsync(CancellationToken ct = default)
    {
        await Wait(150, ct);
        lock (_gate) return _chat.Count(m => !m.FromMe && m.State == ChatState.Held);
    }

    public async Task<ChatMessage> SendChatAsync(string personId, string text, CancellationToken ct = default)
    {
        await Wait(300, ct);
        var person = ChatFixtures.Find(personId) ?? throw new BackstopApiException("You can only message people on your list.");
        var clean = Clean(text);

        StoredChat stored;
        int count;
        lock (_gate)
        {
            stored = new(NewId("c"), person.Id, true, clean, DateTimeOffset.Now, ChatState.Delivered);
            _chat.Add(stored);
            count = _chatSent[person.Id] = _chatSent.GetValueOrDefault(person.Id) + 1;
        }
        Changed?.Invoke();

        // Priya answers from the real /helper view. The others are made up and answer on their own.
        if (person.Id != ChatFixtures.TrustedId) _ = ScriptedReplyAsync(person, count);
        return Shown(stored);
    }

    private async Task ScriptedReplyAsync(ChatPerson person, int count)
    {
        try
        {
            await Task.Delay(2500);
            await ReceiveChatAsync(person, ChatFixtures.Reply(person.Id, count), CancellationToken.None);
        }
        catch (Exception)
        {
            // A reply that cannot be checked is simply not delivered. Nothing is shown on error.
        }
    }

    /// <summary>
    /// A message arrives from someone else. Helpers are trusted and delivered at once. Other users go through the
    /// same rules as mail: the local checks are the floor, Meta AI can only make the result stricter, and any
    /// failure holds the message for a helper.
    /// </summary>
    private async Task ReceiveChatAsync(ChatPerson from, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var stored = new StoredChat(NewId("c"), from.Id, false, text, DateTimeOffset.Now, ChatState.Held);
        if (from.Role == PersonRole.Helper)
        {
            stored.State = ChatState.Delivered;
        }
        else
        {
            var display = Defang.ForUser(text);
            var floor = MockRiskRules.Scan(display);
            var verdict = MockRiskRules.VerdictFor(floor.Score);
            var reason = MockRiskRules.ReasonFor(verdict, floor.TopBecause);
            var flags = new List<RiskFlag>(floor.Flags);
            var checkedOk = true;

            try
            {
                if (llamaChecker is not null)
                {
                    var ai = await llamaChecker.CheckScamAsync(display, null, null, ct);
                    if (!ai.Success)
                    {
                        checkedOk = false;
                        // Never looser than the local floor: a failed check keeps a stricter floor verdict and its reason.
                        if (IsStricter(Verdict.BeCareful, verdict)) { verdict = Verdict.BeCareful; reason = ai.Reason; }
                    }
                    else if (IsStricter(ai.Verdict, verdict))
                    {
                        verdict = ai.Verdict;
                        reason = ai.Reason;
                    }
                    if (ai.Success)
                        foreach (var f in ai.Flags)
                            if (flags.All(x => x.Id != f.Id)) flags.Add(f);
                    if (ai.NeedsHuman || ai.AtRisk) checkedOk = false; // held for the helper
                }
            }
            catch (Exception)
            {
                checkedOk = false;
                if (IsStricter(Verdict.BeCareful, verdict)) verdict = Verdict.BeCareful;
            }

            // A local or AI signal that someone may need help always holds the message for the helper.
            if (flags.Any(f => f.Id is "at-risk" or "review-needed")) checkedOk = false;
            if (!checkedOk && IsStricter(Verdict.BeCareful, verdict)) verdict = Verdict.BeCareful;

            stored.Verdict = verdict;
            stored.Flags = flags.Take(4).ToList();
            stored.Reason = reason;
            stored.State = checkedOk && verdict == Verdict.LooksSafe ? ChatState.Delivered : ChatState.Held;
        }

        lock (_gate)
        {
            _chat.Add(stored);
            if (stored.State == ChatState.Held)
                Log("hourglass", $"A message from {from.Name} is waiting for you to check.");
        }
        Changed?.Invoke();
    }

    // ---------------- Helper side ----------------

    public async Task<IReadOnlyList<HeldChat>> GetHeldChatsAsync(CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate)
            return _chat
                .Where(m => !m.FromMe && m.State == ChatState.Held && ChatFixtures.Find(m.PersonId) is not null)
                .OrderByDescending(m => m.At)
                .Select(m => new HeldChat(m.Id, ChatFixtures.Find(m.PersonId)!, Defang.ForUser(m.Text), m.Verdict, m.Flags, m.Reason, m.At))
                .ToList();
    }

    public async Task ResolveHeldChatAsync(string id, ReviewDecision decision, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate)
        {
            var m = _chat.FirstOrDefault(x => x.Id == id && x.State == ChatState.Held)
                ?? throw new BackstopApiException("That message has already been decided.");
            var who = ChatFixtures.Find(m.PersonId)?.Name ?? "someone";
            switch (decision)
            {
                case ReviewDecision.Release:
                    m.State = ChatState.Delivered;
                    m.At = DateTimeOffset.Now;
                    Log("check", $"You let a message from {who} through to {User}.");
                    break;
                case ReviewDecision.Block:
                    m.State = ChatState.Blocked;
                    Log("cross", $"You blocked a message from {who}.");
                    break;
                // Anything else leaves the message held. It is never delivered by accident.
            }
        }
        Changed?.Invoke();
    }

    public async Task<ChatMessage> SendChatAsHelperAsync(string text, CancellationToken ct = default)
    {
        await Wait(300, ct);
        var clean = Clean(text);
        StoredChat stored;
        lock (_gate)
        {
            stored = new(NewId("c"), ChatFixtures.TrustedId, false, clean, DateTimeOffset.Now, ChatState.Delivered);
            _chat.Add(stored);
        }
        Changed?.Invoke();
        return Shown(stored);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetChatLogAsync(string personId, CancellationToken ct = default)
    {
        await Wait(300, ct);
        if (ChatFixtures.Find(personId) is null) throw new BackstopApiException("That person is not on the list.");
        lock (_gate)
        {
            // The helper's own chat with the user is always theirs to read. Everyone else's needs the permission.
            if (personId != ChatFixtures.TrustedId && !guardian.Allows(HelperCapability.SeeChats))
                throw new BackstopApiException($"{User} has not allowed you to read their chats.");
            return Delivered(personId);
        }
    }

    // ---------------- Helpers ----------------

    // Caller holds the lock.
    private List<ChatMessage> Delivered(string personId) =>
        _chat.Where(m => m.PersonId == personId && m.State == ChatState.Delivered)
             .OrderBy(m => m.At)
             .Select(Shown)
             .ToList();

    private static ChatMessage Shown(StoredChat m) => new(m.Id, m.PersonId, m.FromMe, m.Text, m.At);

    private static string Clean(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0) throw new BackstopApiException("Please type your message first.");
        if (t.Length > MaxChatLength) throw new BackstopApiException("That message is too long. Please make it shorter.");
        return t;
    }
}
