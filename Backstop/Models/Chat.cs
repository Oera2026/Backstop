namespace Backstop.Models;

// Chat between the user, their helpers and other people the helper has approved. MOCK: synthetic people.

public enum PersonRole { Helper, User }

/// <summary>Someone the user is allowed to message. The list is set by the helper; the user cannot add to it.</summary>
public sealed record ChatPerson(string Id, string Name, PersonRole Role, string Relationship);

/// <summary>Delivered is shown. Held waits for a helper. Blocked is final and never shown to the user.</summary>
public enum ChatState { Delivered, Held, Blocked }

/// <summary>A delivered message. <see cref="FromMe"/> is from the user's point of view.</summary>
public sealed record ChatMessage(string Id, string PersonId, bool FromMe, string Text, DateTimeOffset At);

public sealed record ChatThread(ChatPerson Person, string? LastText, DateTimeOffset? LastAt);

/// <summary>A message from another user that a helper has to decide. Text only, links defanged.</summary>
public sealed record HeldChat(
    string Id,
    ChatPerson From,
    string Preview,
    Verdict Verdict,
    IReadOnlyList<RiskFlag> Flags,
    string Reason,
    DateTimeOffset At);
