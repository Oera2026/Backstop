using Backstop.Models;

namespace Backstop.Services.Api;

public sealed record MetaScamAnalysis(
    Verdict Verdict,
    string Reason,
    IReadOnlyList<RiskFlag> Flags,
    bool Success,
    string? FailureNote = null,
    bool NeedsHuman = false,
    bool AtRisk = false,
    FailureInfo? Failure = null,
    // The checker tip "Next time, watch for…", already cleaned by WatchFor.Clean. Null when missing or not safe to show: callers use the fixed library.
    string? WatchFor = null
)
{
    /// <summary>Anything short of "looks safe, no human needed, nobody at risk". Only meaningful when <see cref="Success"/> is true.</summary>
    public bool Worried => Verdict != Verdict.LooksSafe || NeedsHuman || AtRisk;
}

public interface IMetaScamChecker
{
    Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default);

    /// <summary>
    /// Same check, with facts from Backstop's own Link check (the public Try it page only). They are about the addresses in the
    /// message, were not written by the sender, and can only make the answer stricter. A checker that does not know about them
    /// ignores them.
    /// </summary>
    Task<MetaScamAnalysis> CheckScamWithLinksAsync(string? text, IReadOnlyList<string> linkFindings, CancellationToken ct = default) =>
        CheckScamAsync(text, null, null, ct);

    /// <summary>
    /// Same check, with facts from Backstop's own checks: the Link check (if any) and the Sender check (who the message says it is from).
    /// Both are Backstop's wording, not the sender's, and can only make the answer stricter. A checker that does not know about the
    /// sender facts ignores them; the caller's own policy still holds the message.
    /// </summary>
    Task<MetaScamAnalysis> CheckScamWithEvidenceAsync(string? text, IReadOnlyList<string> linkFindings, IReadOnlyList<string> senderFindings, CancellationToken ct = default) =>
        linkFindings.Count > 0 ? CheckScamWithLinksAsync(text, linkFindings, ct) : CheckScamAsync(text, null, null, ct);
}
