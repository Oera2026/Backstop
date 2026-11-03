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
    FailureInfo? Failure = null
);

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
}

// Keep ILlamaScamChecker as a backwards-compatible alias for existing code
public interface ILlamaScamChecker : IMetaScamChecker
{
    Task<LlamaScamAnalysis> CheckScamAsync(string text, CancellationToken ct = default);
}

public sealed record LlamaScamAnalysis(
    Verdict Verdict,
    string Reason,
    IReadOnlyList<RiskFlag> Flags,
    bool Success,
    string? FailureNote = null,
    bool NeedsHuman = false,
    bool AtRisk = false,
    FailureInfo? Failure = null
)
{
    public static implicit operator LlamaScamAnalysis(MetaScamAnalysis m) =>
        new(m.Verdict, m.Reason, m.Flags, m.Success, m.FailureNote, m.NeedsHuman, m.AtRisk, m.Failure);

    public static implicit operator MetaScamAnalysis(LlamaScamAnalysis l) =>
        new(l.Verdict, l.Reason, l.Flags, l.Success, l.FailureNote, l.NeedsHuman, l.AtRisk, l.Failure);
}
