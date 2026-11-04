namespace Backstop.Models;

/// <summary>What kind of thing is waiting. Each has its own word, so the helper always knows what they are looking at.</summary>
public enum HelperItemKind { Safeguarding, Mail, Outgoing, Check, AskMe, Chat, Help, NewPerson, Checker }

/// <summary>Urgent first, then things to decide, then calls the helper promised, then things to know.</summary>
public enum HelperGroup { Urgent, Decide, Calls, Info }

/// <summary>
/// One row on the helper's Waiting page. <see cref="Tone"/> is "block", "check" or "safe"; the row always shows
/// <see cref="Icon"/> and <see cref="Word"/> with it, never colour alone. <see cref="Href"/> is null when the row
/// is dealt with in place (Send it, Got it, I helped).
/// </summary>
public sealed record HelperItem(
    string Id,
    HelperItemKind Kind,
    HelperGroup Group,
    string Label,
    string Title,
    string Detail,
    string Preview,
    DateTimeOffset At,
    string? Href,
    string Tone,
    string Icon,
    string Word,
    bool CallRequested = false);

/// <summary><see cref="WaitingCount"/> is everything that needs the helper, including notices. Information-only rows are not counted.</summary>
public sealed record HelperDigest(IReadOnlyList<HelperItem> Items, int WaitingCount)
{
    public IReadOnlyList<HelperItem> In(HelperGroup group) => Items.Where(i => i.Group == group).ToList();
}

/// <summary>One finished decision, for the history page.</summary>
public sealed record HistoryEntry(DateTimeOffset At, string Icon, string Title, string Outcome, string Tone);

/// <summary>The user got stuck on a step and asked for help. It stays until the helper says they helped.</summary>
public sealed record HelpRequest(string Id, DateTimeOffset At, string Title, int Step, bool Done);

/// <summary>The answer to a helper's one-tap decision. When not Ok, nothing was started and the message stays held.</summary>
public sealed record DecisionResult(bool Ok, string? Error = null);

/// <summary>A decision inside its undo window. Nothing has changed for anyone yet. <see cref="SecondsLeft"/> counts down to 0.</summary>
public sealed record PendingDecision(bool Safe, int SecondsLeft);
