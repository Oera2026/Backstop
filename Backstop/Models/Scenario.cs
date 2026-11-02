namespace Backstop.Models;

/// <summary>An email as it arrives: only who sent it and what it says. Nothing about what it "means".</summary>
public sealed record InboundMail(string FromName, string FromAddress, string Subject, string Body);

/// <summary>
/// A synthetic incoming email for the demo. <see cref="Expected"/> is only what the demo author expects the
/// checks to decide. It is never used to decide anything.
/// </summary>
public sealed record Scenario(
    string Id,
    string Title,
    string Description,
    MessageState Expected,
    InboundMail Mail);

public sealed record OutgoingCheck(bool NeedsPause, IReadOnlyList<string> Reasons);
