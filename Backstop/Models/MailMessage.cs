namespace Backstop.Models;

/// <summary>
/// Life-cycle of every incoming message. Only <see cref="Cleared"/> is ever visible to the user.
/// Anything else (including <see cref="Failed"/> and unknown states) is treated as "not safe yet".
/// </summary>
public enum MessageState { Received, Scanning, Cleared, Held, Blocked, Failed }

/// <summary>How strong the warning signs are. None for cleared mail. It comes from the rules score and the checks that ran, never from a guess.</summary>
public enum Concern { None, Unclear, Some, Strong }

public enum TierStatus { Pending, Running, Passed, Flagged, Skipped, Failed }

/// <summary>One thing that looked wrong, written for the helper in plain language.</summary>
public sealed record Flag(string Icon, string Title, string Explanation);

/// <summary>Result of the link check, by address only (the link is never opened). Signals are only the address checks that found something. The URL is already defanged for display.</summary>
public sealed record LinkCheck(string DefangedUrl, IReadOnlyList<string> Signals);

/// <summary>One stage of the checking pipeline and what it found. No timings: none are invented.</summary>
public sealed record TierResult(string Name, TierStatus Status, string Note);

public sealed class MailMessage
{
    public required string Id { get; init; }
    public required string FromName { get; init; }
    public required string FromAddress { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }

    /// <summary>Where a reply would go, when the sender set a Reply-To. Null when there is none.</summary>
    public string? ReplyTo { get; init; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;

    public MessageState State { get; set; } = MessageState.Received;

    /// <summary>Plain-language summary for the helper's evidence card.</summary>
    public string Summary { get; set; } = "";
    public List<Flag> Flags { get; set; } = [];
    public List<LinkCheck> Links { get; set; } = [];

    /// <summary>What the sender check found about who sent this, most serious first. Evidence for the helper; empty when nothing looked wrong.</summary>
    public List<SenderSignal> SenderSignals { get; set; } = [];
    public Concern Concern { get; set; } = Concern.Unclear;

    /// <summary>The real score from the local rules (sum of the weights of the warning signs found). Not a probability.</summary>
    public int RulesScore { get; set; }
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

    public InboundMail AsInbound() => new(FromName, FromAddress, Subject, Body, ReplyTo);

    public string Preview
    {
        get
        {
            var oneLine = string.Join(' ', Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return oneLine.Length <= 90 ? oneLine : oneLine[..90].TrimEnd() + "…";
        }
    }
}
