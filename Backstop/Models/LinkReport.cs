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
    string Summary,
    // Harmless facts (a shortener, a hop count). They never change the verdict. Signals holds only what raises it.
    IReadOnlyList<string>? Info = null,
    // The well-known site the link ends at, when every hop was clean. Set means "Looks fine: goes to <domain>".
    string? TrustedDomain = null)
{
    /// <summary>True when what was found is a disguise: a bare number, look-alike letters, a name in front of the address, words that name another site, or a hidden destination.</summary>
    public bool Disguised => Signals.Any(s =>
        s.Contains("number instead of a website name") || s.Contains("look-alike") || s.Contains("name in front of the address")
        || s.StartsWith("The link says") || s.Contains("do not recognise"));
}

