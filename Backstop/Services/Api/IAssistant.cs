namespace Backstop.Services.Api;

/// <summary>One line of an Ask Me conversation.</summary>
public sealed record AssistantTurn(bool FromUser, string Text);

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
    Backstop.Models.FailureInfo? Failure = null);

/// <summary>Talks with the user in Ask Me. The only implementation talks to Meta's Muse models.</summary>
public interface IAssistant
{
    Task<AssistantReply> ReplyAsync(IReadOnlyList<AssistantTurn> history, string message, CancellationToken ct = default);
}
