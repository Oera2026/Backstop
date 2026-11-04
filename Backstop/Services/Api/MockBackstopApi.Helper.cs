using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

// What the helper sees on the Waiting and History pages, and the helper-only actions around them.
public sealed partial class MockBackstopApi
{
    private const int MaxPeople = 12;

    /// <summary>Ask Me sent this to the helper because someone may need help or Ask Me could not answer. It is never hidden by a tier.</summary>
    private static bool IsEscalation(StoredCheck c) => c.Result.Flags.Any(f => f.Id is "at-risk" or "review-needed");
    private static bool IsSafeguarding(StoredCheck c) => c.Result.Flags.Any(f => f.Id == "at-risk");

    /// <summary>Check requests need that access. Safeguarding alerts from Ask Me are the floor: every tier gets them.</summary>
    private bool CanSee(StoredCheck c) => IsEscalation(c) || guardian.Allows(HelperCapability.SeeCheckRequests);

    private static (string Tone, string Icon, string Word) MailRisk(MailMessage m) =>
        m.AwaitingScan ? ("check", "refresh", "Not checked yet") :
        m.Concern switch
        {
            Concern.Strong => ("block", "alert", "Looks like a scam"),
            Concern.Some => ("check", "alert", "Might be a scam"),
            _ => ("check", "hourglass", "Not sure"),
        };

    private static (string Tone, string Icon, string Word) VerdictRisk(Verdict v) => v switch
    {
        Verdict.DontReply => ("block", "alert", "Looks like a scam"),
        Verdict.BeCareful => ("check", "alert", "Might be a scam"),
        _ => ("check", "hourglass", "Needs you"),
    };

    public HelperDigest GetHelperDigest()
    {
        var items = new List<HelperItem>();

        foreach (var m in guardian.GetQueue())
        {
            var (tone, icon, word) = MailRisk(m);
            items.Add(new(m.Id, HelperItemKind.Mail, m.CallRequested ? HelperGroup.Calls : HelperGroup.Decide, "Mail", m.FromName, m.Subject,
                m.AwaitingScan ? "The checker could not finish. It will try again." : m.Summary, m.ReceivedAt, $"helper/{m.Id}", tone, icon, word, m.CallRequested));
        }

        foreach (var o in guardian.GetHeldOutgoing())
            items.Add(new(o.Id, HelperItemKind.Outgoing, HelperGroup.Decide, "Being sent", $"{User} wants to send to {Defang.ForHelper(o.ToAddress)}", o.Subject,
                $"{Defang.ForHelper(o.Body)} {string.Join(" ", o.Reasons)}".Trim(), o.At, null, "check", "hourglass", "Held back"));

        foreach (var n in guardian.GetNotices())
            items.Add(new(n.Id, HelperItemKind.NewPerson, HelperGroup.Info, "New person", "Wrote to someone new", n.Address,
                $"First time. Subject: {n.Subject}", n.At, null, "info", "user", ""));

        var waitingForChecker = guardian.GetQueue().Count(m => m.AwaitingScan);
        if (waitingForChecker > 0)
            items.Add(new("checker", HelperItemKind.Checker, HelperGroup.Info, "Checker", waitingForChecker == 1 ? "1 message is waiting for the checker" : $"{waitingForChecker} messages are waiting for the checker",
                "They stay held until the checker answers.", "", DateTimeOffset.Now, null, "check", "refresh", ""));

        lock (_gate)
        {
            foreach (var c in _checks.Values.Where(x => x.Result.Status == ReviewStatus.Held && CanSee(x)))
            {
                var r = FromCheck(c);
                var href = $"helper/check/{c.Result.Id}";
                if (IsSafeguarding(c))
                    items.Add(new(r.Id, HelperItemKind.Safeguarding, HelperGroup.Urgent, "May need help", $"{User} may need help", "Please get in touch with them now.",
                        r.Preview, r.At, href, "block", "alert", "Needs you now", r.CallRequested));
                else if (IsEscalation(c))
                    items.Add(new(r.Id, HelperItemKind.AskMe, r.CallRequested ? HelperGroup.Calls : HelperGroup.Decide, "Ask Me", "Ask Me needs you", $"{User} asked something Ask Me could not answer.",
                        r.Preview, r.At, href, "check", "chat", "Needs you", r.CallRequested));
                else
                {
                    var (tone, icon, word) = VerdictRisk(r.Verdict);
                    items.Add(new(r.Id, HelperItemKind.Check, r.CallRequested ? HelperGroup.Calls : HelperGroup.Decide, "Check", r.Title,
                        r.Flags.Count > 0 ? r.Flags[0].Title : "Asked you to look", r.Preview, r.At, href, tone, icon, word, r.CallRequested));
                }
            }

            foreach (var m in _chat.Where(x => !x.FromMe && x.State == ChatState.Held && FindPerson(x.PersonId) is not null))
            {
                var who = FindPerson(m.PersonId)!;
                var (tone, icon, word) = VerdictRisk(m.Verdict);
                items.Add(new(m.Id, HelperItemKind.Chat, HelperGroup.Decide, "Chat", $"{who.Name} wrote to {User}", m.Flags.Count > 0 ? m.Flags[0].Title : who.Relationship,
                    Defang.ForUser(m.Text), m.At, "helper/messages", tone, icon, word));
            }

            foreach (var h in _help.Where(x => !x.Done))
                items.Add(new(h.Id, HelperItemKind.Help, HelperGroup.Decide, "Help", $"{User} is stuck", h.Step > 0 ? $"Step {h.Step} of \"{h.Title}\"" : h.Title,
                    "", h.At, null, "check", "hand", "Wants help"));
        }

        // Worst first inside each group, then newest.
        var ordered = items
            .OrderBy(i => i.Group)
            .ThenBy(i => i.Tone == "block" ? 0 : 1)
            .ThenByDescending(i => i.At)
            .ToList();
        return new HelperDigest(ordered, ordered.Count(i => i.Kind != HelperItemKind.Checker));
    }

    public IReadOnlyList<HistoryEntry> GetDecisionHistory()
    {
        var list = new List<HistoryEntry>();
        foreach (var m in guardian.GetDecided(500))
        {
            var byChecker = m.State == MessageState.Blocked && m.HelperDecision is null;
            var blocked = m.State == MessageState.Blocked;
            list.Add(new(m.ReceivedAt, blocked ? "cross" : "check", $"{m.FromName}: {m.Subject}",
                byChecker ? "Blocked by the checker" : blocked ? "You blocked it" : "You approved it", blocked ? "block" : "safe"));
        }

        lock (_gate)
        {
            foreach (var c in _checks.Values.Where(x => x.Result.Status is ReviewStatus.Released or ReviewStatus.Blocked))
            {
                var blocked = c.Result.Status == ReviewStatus.Blocked;
                list.Add(new(c.AskedAt ?? c.Result.CheckedAt, blocked ? "cross" : "check", TitleFor(c.Result, c.Original),
                    blocked ? "You blocked it" : "You said it is safe", blocked ? "block" : "safe"));
            }
            foreach (var m in _chat.Where(x => !x.FromMe && (x.Reviewed || x.State == ChatState.Blocked)))
            {
                var blocked = m.State == ChatState.Blocked;
                list.Add(new(m.At, blocked ? "cross" : "check", $"A message from {FindPerson(m.PersonId)?.Name ?? "someone"}",
                    blocked ? "You blocked it" : "You let it through", blocked ? "block" : "safe"));
            }
            foreach (var h in _help.Where(x => x.Done))
                list.Add(new(h.At, "hand", $"{User} was stuck: {h.Title}", "You helped", "safe"));
        }

        foreach (var n in guardian.GetSeenNotices())
            list.Add(new(n.At, "user", $"Wrote to someone new: {n.Address}", "You saw it", "safe"));

        return list.OrderByDescending(e => e.At).ToList();
    }

    public void ResolveHelpRequest(string id)
    {
        lock (_gate)
        {
            var i = _help.FindIndex(h => h.Id == id && !h.Done);
            if (i < 0) return;
            var title = _help[i].Title;
            _help[i] = _help[i] with { Done = true };
            Log("check", $"You helped {User} with \"{title}\".");
        }
        Changed?.Invoke();
    }

    public void MarkCalled(string id)
    {
        var done = false;
        lock (_gate)
        {
            if (_checks.TryGetValue(id, out var c) && c.Result.Status == ReviewStatus.Held && c.Result.CallRequested)
            {
                _checks[id] = c with { Result = c.Result with { CallRequested = false } };
                Log("phone", $"You called {User} about {LowerFirst(TitleFor(c.Result, c.Original))}.");
                done = true;
            }
        }
        if (done) Changed?.Invoke();
        else guardian.CalledBack(id);
    }

    public ChatPerson AddChatPerson(string name, string relationship)
    {
        name = (name ?? "").Trim();
        relationship = (relationship ?? "").Trim();
        if (name.Length == 0) throw new BackstopApiException("Please type their name.");
        if (name.Length > 40 || !InputGuard.IsSingleLine(name, 40)) throw new BackstopApiException("That name is not right. Please use up to 40 letters on one line.");
        if (relationship.Length == 0) relationship = "Someone you know";
        if (relationship.Length > 60 || !InputGuard.IsSingleLine(relationship, 60)) throw new BackstopApiException("Please say who they are in a few words.");
        ChatPerson person;
        lock (_gate)
        {
            if (_people.Count >= MaxPeople) throw new BackstopApiException("The list is full. Take someone off first.");
            if (_people.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new BackstopApiException($"{name} is already on the list.");
            person = new ChatPerson(NewId("p-"), name, PersonRole.User, relationship);
            _people.Add(person);
            Log("plus", $"You added {name} to the people {User} can message.");
        }
        Changed?.Invoke();
        return person;
    }

    public void RemoveChatPerson(string id)
    {
        lock (_gate)
        {
            if (id == ChatFixtures.TrustedId) throw new BackstopApiException("You cannot take yourself off the list.");
            var p = _people.FirstOrDefault(x => x.Id == id);
            if (p is null) return;
            _people.Remove(p);
            Log("minus", $"You took {p.Name} off the people {User} can message.");
        }
        Changed?.Invoke();
    }
}
