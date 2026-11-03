namespace Backstop.Models;

/// <summary>How a link looks. NoWarnings never means "safe": it means nothing was found. Unsafe includes every link that could not be checked.</summary>
public enum LinkVerdict { NoWarnings, Suspicious, Unsafe }

/// <summary>One step of the trip a link takes. The address is already defanged. Note is "redirects", "final page", "blocked" or "no answer".</summary>
public sealed record LinkHop(string DefangedUrl, string Note);

/// <summary>
/// What the Link check found out about one link. Nothing here is stored. <see cref="Fetched"/> is false when the link was
/// never opened (a reserved example address, a blocked address, or a failure). <see cref="Verified"/> is true only when it
/// was opened safely and the whole trip was read. Anything else is "could not verify, treated as unsafe".
/// </summary>
public sealed record LinkReport(
    string StartUrl,
    string? FinalUrl,
    IReadOnlyList<LinkHop> Chain,
    IReadOnlyList<string> Signals,
    LinkVerdict Verdict,
    bool Verified,
    bool Fetched,
    string Summary);
