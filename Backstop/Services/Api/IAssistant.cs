namespace Backstop.Services.Api;

/// <summary>One line of an Ask Me conversation.</summary>
public sealed record AssistantTurn(bool FromUser, string Text);

/// <summary>One piece of reference material for the assistant: an email, a sent message or a note. Untrusted text.</summary>
public sealed record MaterialItem(string Id, string Kind, string Label, string Text);

/// <summary>What the person has open, plus what was found for their question. All of it is data, never instructions.</summary>
public sealed record AssistantMaterial(DateOnly Today, IReadOnlyList<MaterialItem> Items);

/// <summary>A draft as the assistant wrote it. <see cref="ToName"/> is only a name; the app decides the address.</summary>
public sealed record AssistantDraft(string? ToName, string Subject, string Body);

/// <summary>A calendar entry as the assistant wrote it. The app checks every part.</summary>
public sealed record AssistantEvent(string Title, DateOnly Date, TimeOnly? Time, string Place);

/// <summary>
/// What the assistant said. <see cref="Text"/> has already been checked for display. When <see cref="Success"/> is
/// false there is no usable text and the caller says so plainly. <see cref="NeedsHuman"/> and <see cref="AtRisk"/>
/// can be true even then: a helper is told whenever either is set.
/// </summary>
public sealed record AssistantReply(
    string Text,
    bool Success,
    bool NeedsHuman = false,
    bool AtRisk = false,
    string? Suggest = null,
    string? FailureNote = null,
    AssistantDraft? Draft = null,
    AssistantEvent? Event = null,
    IReadOnlyList<string>? Uses = null);

/// <summary>Talks with the user in Ask Me. The only implementation talks to Meta's Muse models.</summary>
public interface IAssistant
{
    Task<AssistantReply> ReplyAsync(IReadOnlyList<AssistantTurn> history, string message, AssistantMaterial? material = null, CancellationToken ct = default);
}
