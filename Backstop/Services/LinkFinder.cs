using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services;

/// <summary>
/// Finds links in text and judges each one by its address only. Real mail links are never opened (only /try uses ILinkInspector).
/// Finds full links (https://…), www. links and bare addresses (bit.ly/x, shop.example.net/book).
/// </summary>
public static class LinkFinder
{
    public const int MaxLinks = 10;
    public const int MaxLength = 200;

    public sealed record Scan(IReadOnlyList<LinkCheck> Links, int Problems, bool Deceptive);

    // Bare addresses need a known ending, or a path after any ending, so "end.Next" or "3.5" are not mistaken for links. Not after @ (an email address).
    // Short endings that are also everyday words (.to, .it, .in) count only with a path, so "Thanks.It was lovely" keeps its words.
    public const string PatternText =
        @"\b(?:https?|ftps?|sftp|file|smb|ssh|telnet)://[^\s<>""')\]]+|\bwww\.[^\s<>""')\]]+" +
        @"|(?<![@\w./-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.){1,12}(?:(?:com|co|org|net|gov|ac|edu)\.[a-z]{2}(?![a-z0-9-])|com|net|org|uk|gov|edu|info|biz|io|ly|gl|gd|xyz|top|click|link|live|online|site|shop|store|app|dev|test|ru|cn|tk|cc|me|ai|support|help|de|fr|eu|ca|au|icu|vip|club|cam|cloud|website|mobi|tech|ws|pw|su|ml|ga|cf|gq|[a-z]{2,24}(?=[:/]))(?![\w@-])(?::\d{1,5})?(?:/[^\s<>""')\]]*)?" +
        @"|(?<![@\w./-])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\w@-]|\.\d)(?::\d{1,5})?(?:/[^\s<>""')\]]*)?";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // A stuck match must fail fast and closed, never hang the check.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Any link in the text.</summary>
    public static Regex Pattern { get; } = new(PatternText, Options, MatchTimeout);

    /// <summary>Any link, or the marker left where one was taken out.</summary>
    public static Regex PatternOrRemoved { get; } = new(@"\[link removed\]|" + PatternText, Options, MatchTimeout);

    private static readonly string[] ShortenerHosts =
        ["bit.ly", "tinyurl.com", "t.co", "goo.gl", "is.gd", "ow.ly", "rb.gy", "cutt.ly", "buff.ly", "shorturl.at"];
    private static readonly string[] ShortenerLabels = ["short", "shorten", "tiny", "tinyurl"];
    private static readonly Regex RiskyWords = new(
        @"\b(?:login|log-in|signin|sign-in|verify|password|download|claim|reward|prize)s?\b", Options, MatchTimeout);

    /// <summary>Every distinct link in the text (up to <see cref="MaxLinks"/>), judged by its address.</summary>
    public static Scan Inspect(string text)
    {
        try
        {
            // As written first, so look-alike letters in an address are still seen. Then folded, so hidden characters cannot hide a link at all.
            var all = Pattern.Matches(text).Concat(Pattern.Matches(InputGuard.Fold(text)))
                .Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?')).Where(v => v.Length > 0).Distinct().ToList();
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
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: one problem, so the message is held and never cleared.
            return new Scan([new LinkCheck("[could not check]", [CouldNotCheckNote])], 1, false);
        }
    }

    /// <summary>Returns the address signals that fired (empty when none did), whether any is a problem, and whether the address is disguised.</summary>
    public const string ShortenerNote = "It is a shortened link, so the real website is hidden.";
    public const string NotSecureNote = "It is not a secure page.";
    public const string RiskyWordsNote = "The address mentions signing in, downloading or a prize.";
    public const string VeryLongNote = "The address is very long.";
    public const string CouldNotCheckNote = "The link could not be fully checked.";
    public const string NotWebNote = "It is not an ordinary web address.";

    /// <param name="maxLength">Real mail uses <see cref="MaxLength"/>. The Link check on /try allows longer ones, because shop and
    /// tracking links are often several hundred letters long and that alone says nothing.</param>
    public static (IReadOnlyList<string> Signals, bool Problem, bool Disguised) Judge(string raw, int maxLength = MaxLength)
    {
        if (raw.Length > maxLength) return ([VeryLongNote], true, true);

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
        { notes.Add(ShortenerNote); disguised = true; }
        if (hasScheme && uri.Scheme == Uri.UriSchemeHttp)
            notes.Add(NotSecureNote);
        if (hasScheme && uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            notes.Add(NotWebNote);
        try
        {
            if (RiskyWords.IsMatch(host + uri.AbsolutePath))
                notes.Add(RiskyWordsNote);
        }
        catch (RegexMatchTimeoutException)
        {
            return ([CouldNotCheckNote], true, false);
        }

        // A plain http page alone is not enough to worry about.
        var problem = notes.Any(n => n != NotSecureNote);
        return (notes, problem, disguised);
    }
}
