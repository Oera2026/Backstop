namespace Backstop.Models;

/// <summary>One line of the Ask Me conversation, passed back so the assistant can follow what was said.</summary>
public sealed record AskTurn(bool FromUser, string Text);

/// <summary>What the person is looking at while they ask: the message they have open, or the message they are writing.</summary>
public sealed record AskContext(
    DateOnly Today,
    string? MessageId = null,
    string? DraftTo = null,
    string? DraftSubject = null,
    string? DraftBody = null);

/// <summary>
/// A suggested message. Nothing is sent: the person opens it in Compose, reads it, and presses Send themselves.
/// <see cref="ReplyToId"/> is a Cleared message the draft answers; the address then comes from that message.
/// <see cref="To"/> only ever comes from the person's own contacts, never from the AI or from message text.
/// </summary>
public sealed record AskDraft(string? To, string Subject, string Body, string? ReplyToId = null);

/// <summary>A suggested calendar entry. Nothing is saved until the person presses Save on the calendar screen.</summary>
public sealed record AskEvent(string Title, DateOnly Date, TimeOnly? Time, string Place);

/// <summary>A link to something in the app that the answer used, such as an email or a note.</summary>
public sealed record AskLink(string Text, string Href);

/// <summary>What Ask Me says back. <see cref="Href"/> is a page in the app, never an address from the message.</summary>
public sealed record AskReply(
    string Text,
    string? Href = null,
    string? LinkText = null,
    bool HelperTold = false,
    AskDraft? Draft = null,
    AskEvent? Event = null,
    IReadOnlyList<AskLink>? Found = null);
