namespace Backstop.Models;

/// <summary>
/// Life-cycle of every incoming message. Only <see cref="Cleared"/> is ever visible to the user.
/// Anything else (including <see cref="Failed"/> and unknown states) is treated as "not safe yet".
/// </summary>
public enum MessageState { Received, Scanning, Cleared, Held, Blocked, Failed }

public enum Confidence { Low, Medium, High }

public enum TierStatus { Pending, Running, Passed, Flagged, Skipped, Failed }

/// <summary>One thing that looked wrong, written for the helper in plain language.</summary>
public sealed record Flag(string Icon, string Title, string Explanation);

/// <summary>Result of the link check (by address only; the link is never opened). All URLs are already defanged for display.</summary>
public sealed record LinkCheck(string DefangedUrl, IReadOnlyList<string> Hops, string? TrackerWarning, string Verdict);

/// <summary>One stage of the checking pipeline and how long it took.</summary>
public sealed record TierResult(string Name, int LatencyMs, TierStatus Status, string Note);

public sealed class MailMessage
{
    public required string Id { get; init; }
    public required string FromName { get; init; }
    public required string FromAddress { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;

    public MessageState State { get; set; } = MessageState.Received;

    /// <summary>Plain-language summary for the helper's evidence card.</summary>
    public string Summary { get; set; } = "";
    public List<Flag> Flags { get; set; } = [];
    public List<LinkCheck> Links { get; set; } = [];
    public Confidence Confidence { get; set; } = Confidence.Low;
    public List<TierResult> Tiers { get; set; } = [];

    /// <summary>
    /// Held because the AI check did not finish. It stays withheld and is scanned again until the AI answers.
    /// A helper cannot approve it while this is set (Block is still allowed).
    /// </summary>
    public bool AwaitingScan { get; set; }

    /// <summary>The helper chose "Call them first". The message stays held.</summary>
    public bool CallRequested { get; set; }

    /// <summary>"Approved" or "Blocked" when a person decided; null when the system decided.</summary>
    public string? HelperDecision { get; set; }

    public InboundMail AsInbound() => new(FromName, FromAddress, Subject, Body);

    public string Preview
    {
        get
        {
            var oneLine = string.Join(' ', Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return oneLine.Length <= 90 ? oneLine : oneLine[..90].TrimEnd() + "…";
        }
    }
}
