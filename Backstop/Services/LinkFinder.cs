using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services;

/// <summary>
/// Finds links in text and judges each one by its address only. A link is never opened, because the app may only
/// talk to Meta and Supabase. Finds full links (https://…), www. links and bare addresses (bit.ly/x, shop.example.net/book).
/// </summary>
public static class LinkFinder
{
    public const int MaxLinks = 10;
    public const int MaxLength = 200;

    public sealed record Scan(IReadOnlyList<LinkCheck> Links, int Problems, bool Deceptive);

    // Bare addresses need a known ending, so "end.Next" or "3.5" are not mistaken for links. Not after @ (an email address).
    public const string PatternText =
        @"\b(?:https?://|www\.)[^\s<>""')\]]+" +
        @"|(?<![@\w./-])(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:com|net|org|uk|gov|edu|info|biz|io|ly|gl|gd|xyz|top|click|link|live|online|site|shop|store|app|dev|test|ru|cn|tk|cc|co(?=/))(?![\w@-])(?:/[^\s<>""')\]]*)?";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Any link in the text.</summary>
    public static Regex Pattern { get; } = new(PatternText, Options);

    /// <summary>Any link, or the marker left where one was taken out.</summary>
    public static Regex PatternOrRemoved { get; } = new(@"\[link removed\]|" + PatternText, Options);

    private static readonly string[] ShortenerHosts =
        ["bit.ly", "tinyurl.com", "t.co", "goo.gl", "is.gd", "ow.ly", "rb.gy", "cutt.ly", "buff.ly", "shorturl.at"];
    private static readonly string[] ShortenerLabels = ["short", "shorten", "tiny", "tinyurl"];
    private static readonly Regex RiskyWords = new(
        @"\b(?:login|log-in|signin|sign-in|verify|password|download|claim|reward|prize)s?\b", Options);

    /// <summary>Every distinct link in the text (up to <see cref="MaxLinks"/>), judged by its address.</summary>
    public static Scan Inspect(string text)
    {
        var all = Pattern.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?')).Where(v => v.Length > 0).Distinct().ToList();
        var links = new List<LinkCheck>();
        var problems = 0;
        var deceptive = false;
        foreach (var raw in all.Take(MaxLinks))
        {
            var (signals, bad, trick) = Judge(raw);
            var shown = Defang.Url(raw.Length > MaxLength ? raw[..MaxLength] : raw);
            links.Add(new LinkCheck(shown, signals));
            if (bad) problems++;
            deceptive |= trick;
        }
        if (all.Count > MaxLinks)
        {
            links.Add(new LinkCheck($"{all.Count - MaxLinks} more", ["There are a lot of links in this message."]));
            problems++;
        }
        return new Scan(links, problems, deceptive);
    }

    /// <summary>Returns the address signals that fired (empty when none did), whether any is a problem, and whether the address is disguised.</summary>
    public static (IReadOnlyList<string> Signals, bool Problem, bool Disguised) Judge(string raw)
    {
        if (raw.Length > MaxLength) return (["The address is very long."], true, true);

        var hasScheme = raw.Contains("://");
        var candidate = hasScheme ? raw : "http://" + raw;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return (["The address is not a proper web address."], true, true);

        var host = uri.IdnHost.ToLowerInvariant();
        var bare = host.StartsWith("www.") ? host[4..] : host;
        var notes = new List<string>();
        var disguised = false;

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        { notes.Add("It uses a number instead of a website name."); disguised = true; }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        { notes.Add("It has a name in front of the address, a trick to look like another site."); disguised = true; }
        if (host.Contains("xn--"))
        { notes.Add("It uses look-alike letters."); disguised = true; }
        if (ShortenerHosts.Any(s => bare == s || bare.EndsWith("." + s)) || ShortenerLabels.Contains(bare.Split('.')[0]))
        { notes.Add("It is a shortened link, so the real website is hidden."); disguised = true; }
        if (hasScheme && uri.Scheme == Uri.UriSchemeHttp)
            notes.Add("It is not a secure page.");
        if (RiskyWords.IsMatch(host + uri.AbsolutePath))
            notes.Add("The address mentions signing in, downloading or a prize.");

        // A plain http page alone is not enough to worry about.
        var problem = notes.Any(n => n != "It is not a secure page.");
        return (notes, problem, disguised);
    }
}
