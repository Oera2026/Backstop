namespace Backstop.Models;

/// <summary>What Backstop would do with a message. Safe is only ever shown when every check passed and Meta AI agreed.</summary>
public enum TryVerdict { Safe, Held, Blocked }

/// <summary>One thing that looked wrong. Link signals are about the address only: the link is never opened.</summary>
public sealed record TrySignal(string Title, string Detail);

/// <summary>
/// The answer to "would Backstop stop this?" for the public Try it page. Nothing about it is stored.
/// <see cref="Problem"/> is set when no check was run (too long, empty, too many tries): there is then no verdict to show.
/// <see cref="Verified"/> is false when Meta AI did not answer. That is always Held, never Safe.
/// </summary>
public sealed record TryResult(
    TryVerdict Verdict,
    bool Verified,
    IReadOnlyList<TrySignal> Signals,
    string Explanation,
    string HelperSees,
    string? Problem = null,
    IReadOnlyList<LinkReport>? Links = null,
    IReadOnlyList<SenderSignal>? SenderChecks = null)
{
    public static TryResult Refused(string problem) => new(TryVerdict.Held, false, [], "", "", problem);
}

/// <summary>A made-up example for the Try it page. It carries no outcome: the real checks decide.</summary>
public sealed record TrySample(string Id, string Label, InboundMail Mail, bool Outgoing = false);
