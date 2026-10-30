using Backstop.Models;

namespace Backstop.Services.Api;

public sealed record MetaScamAnalysis(
    Verdict Verdict,
    string Reason,
    IReadOnlyList<RiskFlag> Flags,
    bool Success,
    string? FailureNote = null
);

public interface IMetaScamChecker
{
    Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default);
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
    string? FailureNote = null
)
{
    public static implicit operator LlamaScamAnalysis(MetaScamAnalysis m) =>
        new(m.Verdict, m.Reason, m.Flags, m.Success, m.FailureNote);

    public static implicit operator MetaScamAnalysis(LlamaScamAnalysis l) =>
        new(l.Verdict, l.Reason, l.Flags, l.Success, l.FailureNote);
}
