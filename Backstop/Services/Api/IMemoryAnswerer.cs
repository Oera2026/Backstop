using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>What the answerer sends back. Uses holds positions in the notes it was given; Found false means the notes did not say.</summary>
public sealed record MemoryDraft(bool Success, bool Found, string Answer, IReadOnlyList<int> Uses, FailureInfo? Failure = null);

/// <summary>Answers a question about the person's saved memories, using only those memories. The real one is <see cref="MetaMemoryAnswerer"/>.</summary>
public interface IMemoryAnswerer
{
    Task<MemoryDraft> AnswerAsync(string question, IReadOnlyList<MemoryCard> cards, CancellationToken ct = default);
}
