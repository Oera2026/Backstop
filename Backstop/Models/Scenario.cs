namespace Backstop.Models;

/// <summary>An email as it arrives: only who sent it and what it says. Nothing about what it "means". ReplyTo is where an answer would go, when the sender set one.</summary>
public sealed record InboundMail(string FromName, string FromAddress, string Subject, string Body, string? ReplyTo = null);

/// <summary>
/// A synthetic example email for the demo. It carries no outcome: the real checks decide, from its text.
/// </summary>
public sealed record Scenario(
    string Id,
    string Title,
    string Description,
    InboundMail Mail);

public sealed record OutgoingCheck(bool NeedsPause, IReadOnlyList<string> Reasons);
