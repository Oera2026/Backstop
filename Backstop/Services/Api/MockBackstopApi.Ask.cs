using Backstop.Models;

namespace Backstop.Services.Api;

public sealed partial class MockBackstopApi
{
    private const int MaxAskLength = 1000;

    /// <summary>
    /// Ask Me. The AI writes the answer. The local rules run first and can only add care: a sign that someone may
    /// be in danger always tells the helper, whatever the AI says, and if the AI cannot answer the person is told so
    /// plainly. Nothing here is a canned answer standing in for the AI.
    /// </summary>
    public async Task<AskReply> AskAsync(IReadOnlyList<AskTurn> history, string message, AskContext? context = null, CancellationToken ct = default)
    {
        await Wait(200, ct);
        var text = (message ?? "").Trim();
        if (text.Length == 0) throw new BackstopApiException("Please type something first.");
        if (text.Length > MaxAskLength) throw new BackstopApiException("That is a bit long for me. Please make it shorter.");

        var floor = MockRiskRules.Scan(Defang.ForUser(text));
        var localRisk = floor.Flags.Any(f => f.Id == "at-risk");
        var looksScammy = floor.Flags.Any(f => f.Id is not ("at-risk" or "injection" or "link")) || LinkFinder.Inspect(text).Problems > 0;

        var today = context?.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var (material, links) = GatherMaterial(text, context, today);
        var openMessage = string.IsNullOrWhiteSpace(context?.MessageId) ? null : mail?.GetClearedMessage(context.MessageId);

        AssistantReply reply;
        if (assistant is null) reply = new("", false, FailureNote: "No assistant configured",
            Failure: new("Ask Me is not switched on yet. Please tell the person who looks after this app.", "Code: NO_ASSISTANT\nWhat: No IAssistant is registered."));
        else
        {
            try { reply = await assistant.ReplyAsync([.. history.Select(t => new AssistantTurn(t.FromUser, t.Text))], text, material, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { reply = new("", false, FailureNote: "Assistant failed",
                Failure: new("Something went wrong. Please try again in a minute.", $"Code: ASSISTANT_EXCEPTION\nWhat: {ex.GetType().Name}\nTime: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")); }
        }

        // Someone may be in danger, or the assistant wants a human: the helper is always told.
        var told = localRisk || reply.NeedsHuman || reply.AtRisk;
        if (told) Escalate(text, atRisk: localRisk || reply.AtRisk, history);

        string answer;
        if (reply.Success)
        {
            answer = reply.Text;
            if (told && !answer.Contains("helper", StringComparison.OrdinalIgnoreCase) && !answer.Contains(Helper, StringComparison.OrdinalIgnoreCase))
                answer += $" I have told {Helper}.";
        }
        else
        {
            // Say what went wrong, in plain words. The technical detail travels separately.
            var plain = reply.Failure?.Message ?? "Sorry, I could not think of an answer just now. Please try again in a moment.";
            answer = told ? $"{plain} I have asked {Helper} to look at your message." : plain;
        }

        // Only fixed pages of the app can be suggested, never anything taken from the message or the reply.
        // An email that itself looks like a scam is never helped with: no draft, and the person is pointed to Check.
        var openText = openMessage is null ? null : openMessage.Subject + "\n" + openMessage.Body;
        var openLooksScammy = openText is not null &&
            (MockRiskRules.Scan(Defang.ForUser(openText)).Flags.Any(f => f.Id is not ("at-risk" or "injection" or "link")) || LinkFinder.Inspect(openText).Problems > 0);
        var suggest = looksScammy || openLooksScammy ? "check" : reply.Suggest;
        var (href, link) = suggest switch
        {
            "check" => ("check", "Check a message"),
            "explain" => ("explain", "Explain something"),
            "show-me" => ("show-me", "Show me how"),
            "story" => ("stories/new", "Make a story"),
            _ => ((string?)null, (string?)null),
        };
        var draft = reply.Success && !openLooksScammy ? ResolveDraft(reply.Draft, openMessage, context) : null;
        var evt = reply.Success ? ResolveEvent(reply.Event, today) : null;
        var found = reply.Success
            ? (reply.Uses ?? []).Where(links.ContainsKey).Distinct().Take(3).Select(id => links[id]).ToList()
            : [];
        return new AskReply(answer, href, link, told, draft, evt, found.Count > 0 ? found : null, reply.Success ? null : reply.Failure);
    }

    // ---------------- Material for the assistant: the open email, earlier mail, sent mail and notes ----------------

    private const int MaxFromMail = 4, MaxFromSent = 2, MaxFromNotes = 3;

    private static readonly HashSet<string> AskStopWords =
    [
        "email", "emails", "mail", "message", "messages", "please", "about", "from", "remind", "show", "find", "tell", "write",
        "draft", "reply", "note", "notes", "explain", "when", "where", "who", "which", "have", "has", "had", "week", "month",
        "ago", "previous", "earlier", "some", "any", "there", "were", "are", "is", "got", "get", "was", "his", "her", "them",
        "they", "can", "could", "would", "should", "need", "want", "like", "just", "into", "than", "then", "also", "again", "send",
    ];

    /// <summary>
    /// Finds what the question is about: the email that is open, the writing in progress, matching mail (Cleared mail
    /// only, because <see cref="IMailService.Search"/> never returns anything else) and matching notes. Everything
    /// goes to the AI as data. <c>links</c> maps an item id to a page in the app, used only for items the AI names.
    /// </summary>
    private (AssistantMaterial? Material, Dictionary<string, AskLink> Links) GatherMaterial(string text, AskContext? context, DateOnly today)
    {
        var links = new Dictionary<string, AskLink>();
        if (mail is null) return (null, links);

        var items = new List<MaterialItem>();
        var open = string.IsNullOrWhiteSpace(context?.MessageId) ? null : mail.GetClearedMessage(context.MessageId);
        if (open is not null)
            items.Add(new("m0", "the email the person has open", $"from {open.FromName}, subject \"{open.Subject}\", received {open.ReceivedAt:yyyy-MM-dd}", Defang.ForUser(open.Body)));
        if (!string.IsNullOrWhiteSpace(context?.DraftBody))
            items.Add(new("d0", "the message the person is writing now", $"subject \"{context.DraftSubject}\"", context.DraftBody));

        var lower = text.ToLowerInvariant();
        var terms = Words().Matches(lower).Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w) && !AskStopWords.Contains(w)).Distinct().Take(6).ToList();

        // Mail and sent mail: more matching words ranks higher, then newer.
        var mailHits = new Dictionary<string, (MailMessage Msg, int Hits)>();
        var sentHits = new Dictionary<string, (SentMessage Msg, int Hits)>();
        foreach (var term in terms)
        {
            var found = mail.Search(term);
            foreach (var m in found.Messages.Where(m => m.Id != open?.Id))
                mailHits[m.Id] = (m, mailHits.TryGetValue(m.Id, out var a) ? a.Hits + 1 : 1);
            foreach (var m in found.Sent)
                sentHits[m.Id] = (m, sentHits.TryGetValue(m.Id, out var a) ? a.Hits + 1 : 1);
        }

        var inbox = mailHits.Values.OrderByDescending(x => x.Hits).ThenByDescending(x => x.Msg.ReceivedAt).Take(MaxFromMail).Select(x => x.Msg).ToList();
        var asksForRecent = new[] { "recent", "latest", "newest", "new email", "new message", "inbox", "last email", "last message" }.Any(lower.Contains);
        if (inbox.Count == 0 && asksForRecent)
            inbox = mail.GetInbox().Where(m => m.Id != open?.Id).OrderByDescending(m => m.ReceivedAt).Take(3).ToList();

        var n = 0;
        foreach (var m in inbox)
        {
            var id = $"m{++n}";
            items.Add(new(id, "earlier email", $"from {m.FromName}, subject \"{m.Subject}\", received {m.ReceivedAt:yyyy-MM-dd}", Defang.ForUser(m.Body)));
            links[id] = new AskLink($"Email from {m.FromName}: {Trim(m.Subject, 40)}", $"message/{m.Id}");
        }

        n = 0;
        foreach (var m in sentHits.Values.OrderByDescending(x => x.Hits).ThenByDescending(x => x.Msg.SentAt).Take(MaxFromSent).Select(x => x.Msg))
        {
            var id = $"s{++n}";
            items.Add(new(id, "message the person sent", $"to {(m.ToName.Length > 0 ? m.ToName : "someone")}, subject \"{m.Subject}\", sent {m.SentAt:yyyy-MM-dd}", Defang.ForUser(m.Body)));
            links[id] = new AskLink($"You wrote to {(m.ToName.Length > 0 ? m.ToName : "someone")}: {Trim(m.Subject, 40)}", $"sent/{m.Id}");
        }

        // Notes are the cards in Remember.
        List<MemoryCard> notes;
        lock (_gate)
        {
            notes = _memories.Select(c => (Card: c, Score: Score(c, text))).Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.Card.Date).Take(MaxFromNotes).Select(x => x.Card).ToList();
            if (notes.Count == 0 && new[] { "note", "notes", "remember", "saved" }.Any(lower.Contains))
                notes = _memories.OrderByDescending(c => c.Date).Take(MaxFromNotes).ToList();
        }
        n = 0;
        foreach (var c in notes)
        {
            var id = $"n{++n}";
            items.Add(new(id, "note", $"\"{c.Title}\", saved {c.Date:yyyy-MM-dd}", $"{c.Summary} {c.HowYouDidIt} Steps: {string.Join("; ", c.Steps)}"));
            links[id] = new AskLink($"Note: {Trim(c.Title, 40)}", $"remember/{c.Id}");
        }

        return (new AssistantMaterial(today, items), links);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    /// <summary>
    /// The AI only writes words. The address is decided here: the open Cleared email's sender (a reply), or a contact
    /// the person already has, or nothing at all. An address the AI or an email suggests is never used.
    /// </summary>
    private AskDraft? ResolveDraft(AssistantDraft? d, MailMessage? open, AskContext? context)
    {
        if (d is null || mail is null) return null;
        var subject = d.Subject;

        bool NameMatches(string? name, string other) =>
            !string.IsNullOrWhiteSpace(name) && (other.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(other, StringComparison.OrdinalIgnoreCase));

        if (open is not null && !NoReply.IsNoReply(open.FromAddress) && (string.IsNullOrWhiteSpace(d.ToName) || NameMatches(d.ToName, open.FromName)))
        {
            if (!subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) subject = "Re: " + open.Subject;
            return new AskDraft(null, subject, d.Body, open.Id);
        }

        var contact = string.IsNullOrWhiteSpace(d.ToName) ? null
            : mail.GetContacts().FirstOrDefault(c => NameMatches(d.ToName, c.Name) && !NoReply.IsNoReply(c.Address));
        if (contact is not null) return new AskDraft(contact.Address, subject, d.Body);

        // Improving a message already being written: keep who it was for.
        if (string.IsNullOrWhiteSpace(d.ToName) && InputGuard.IsPlainAddress(context?.DraftTo) && !NoReply.IsNoReply(context!.DraftTo))
            return new AskDraft(context.DraftTo, subject, d.Body);

        return new AskDraft(null, subject, d.Body);
    }

    /// <summary>A date in the past, or years away, is more likely a mistake than a plan.</summary>
    private static AskEvent? ResolveEvent(AssistantEvent? e, DateOnly today) =>
        e is null || e.Date < today || e.Date > today.AddYears(2) ? null : new AskEvent(e.Title, e.Date, e.Time, e.Place);

    /// <summary>Puts the conversation in the helper's queue, held, so a person looks at it.</summary>
    private void Escalate(string text, bool atRisk, IReadOnlyList<AskTurn> history)
    {
        // The helper reads the last few turns, not only the final message, so they can see how it got here.
        var original = string.Join("\n", history.TakeLast(6).Select(t => $"{(t.FromUser ? User : "Ask Me")}: {t.Text}").Append($"{User}: {text}"));
        var flag = atRisk
            ? MockRiskRules.AllFlags.First(f => f.Id == "at-risk")
            : new RiskFlag("review-needed", "hourglass", "Needs helper check", "Your helper will take a look at this.");
        var result = new CheckResult(NewId("chk"), CheckKind.Message, Verdict.BeCareful,
            atRisk ? "Someone may need help." : "Ask Me was not sure how to help.",
            [flag], Defang.ForUser(text), [], null, ReviewStatus.Held, false, DateTimeOffset.Now);
        lock (_gate)
        {
            _checks[result.Id] = new(result, original, DateTimeOffset.Now);
            MarkVisitorItem(result.Id);
        }
        RaiseChanged();
    }
}
