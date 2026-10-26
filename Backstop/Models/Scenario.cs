namespace Backstop.Models;

/// <summary>A synthetic incoming email for the demo, plus what the mock pipeline "finds".</summary>
public sealed record Scenario(
    string Id,
    string Title,
    string Description,
    MessageState Outcome,
    string FromName,
    string FromAddress,
    string Subject,
    string Body,
    string Summary,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<LinkCheck> Links,
    Confidence Confidence,
    IReadOnlyList<TierResult> Tiers);

public sealed record OutgoingCheck(bool NeedsPause, IReadOnlyList<string> Reasons);
