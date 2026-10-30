using Backstop.Models;

namespace Backstop.Services.Api;

public sealed record LlamaScamAnalysis(
    Verdict Verdict,
    string Reason,
    IReadOnlyList<RiskFlag> Flags,
    bool Success,
    string? FailureNote = null
);

public interface ILlamaScamChecker
{
    Task<LlamaScamAnalysis> CheckScamAsync(string text, CancellationToken ct = default);
}
