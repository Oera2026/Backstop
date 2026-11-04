using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services.Links;

/// <summary>Opens a link safely and says what it found. Used by the public Try it page only. Real mail links are never opened.</summary>
public interface ILinkInspector
{
    /// <param name="shownText">The words the message shows for the link, if any, so they can be compared with where it really goes.</param>
    Task<LinkReport> InspectAsync(string url, string? shownText = null, CancellationToken ct = default);
}

/// <summary>
/// Follows a link the way a careful person would, without ever running or drawing the page:
/// GET only, http and https on ports 80 and 443, at most 5 redirects, 5 seconds a hop, 1 MB read, no cookies, no sign-in headers.
/// Every hop looks the name up again and refuses any address that is not an ordinary public one, and the connection itself
/// (see <see cref="CreateClient"/>) looks it up again and only dials an address from an all-public answer.
/// The page text is read only to look for a password box. It is never kept and never passed on.
/// Any timeout, error or refused address ends as "could not verify, treated as unsafe".
/// </summary>
public sealed partial class LinkInspector : ILinkInspector
{
    public const int MaxRedirects = 5;
    public const int MaxBodyBytes = 1024 * 1024;
    public const int MaxLinksChecked = 3;

    public TimeSpan HopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Reserved example addresses (example.com and friends, .test) are judged by address only. Off in tests that need to open them.</summary>
    public bool SkipReservedExamples { get; init; } = true;

    private readonly HttpMessageInvoker _client;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly ILinkReputation? _reputation;
    private readonly ILogger? _logger;

    public LinkInspector(HttpMessageInvoker client, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        ILinkReputation? reputation = null, ILogger? logger = null)
    {
        _client = client;
        _resolve = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
        _reputation = reputation;
        _logger = logger;
    }

    /// <summary>
    /// The real client: no redirects followed by the handler, no cookies, no proxy, no decompression, and a connect step that
    /// looks the name up itself and refuses anything that is not a public address. That is what stops a name that answers one
    /// way to the check and another way to the visit (DNS rebinding).
    /// </summary>
    public static HttpMessageInvoker CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host;
            if (ctx.DnsEndPoint.Port is not (80 or 443)) throw new HttpRequestException("Blocked port");
            var addresses = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !IpGuard.IsPublic(a))) throw new HttpRequestException("Blocked address");
            var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[0], ctx.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    });

    /// <summary>
    /// What a link check found, in three tiers. Info is harmless and never raises the verdict (a shortener, a redirect, a hop count).
    /// Medium holds the message (a shortener or redirect that ends somewhere we do not know). High is dangerous.
    /// </summary>
    private sealed class Found
    {
        public readonly List<string> High = [], Medium = [], Info = [];
        public bool SawShortener;
    }

    // Links in shop mail can be several hundred letters long with tracking in them. Only an absurd length is worth a word.
    private const int MaxJudgedLength = 2000;

    public async Task<LinkReport> InspectAsync(string url, string? shownText = null, CancellationToken ct = default)
    {
        var raw = (url ?? "").Trim();
        var start = raw.Contains("://") ? raw : "https://" + raw;
        var shownStart = Shown(raw);
        var found = new Found();
        var chain = new List<LinkHop>();

        if (!Uri.TryCreate(start, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return Fail(shownStart, chain, found, "The address is not a proper web address.");

        var startHost = uri.IdnHost.ToLowerInvariant();

        if (SkipReservedExamples && IpGuard.IsReservedExample(startHost))
        {
            JudgeHop(uri, found);
            if (HasMismatch(shownText, startHost, startHost, out var exampleMismatch)) found.High.Add(exampleMismatch!);
            chain.Add(new LinkHop(Shown(uri.AbsoluteUri), "not opened"));
            return Done(shownStart, null, chain, found, verified: false, fetched: false, trusted: null,
                "Example address: only the address was checked.", unsafeIfUnverified: false);
        }

        var current = uri;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redirects = 0;

        // Every way out of the loop that is not a finished check. Whatever was found on the way is kept.
        LinkReport Bail(string why)
        {
            if (HasMismatch(shownText, startHost, current.IdnHost.ToLowerInvariant(), out var m)) found.High.Add(m!);
            return Fail(shownStart, chain, found, why);
        }

        while (true)
        {
            // Every hop is judged, not only the last: a step that is not secure or is a bare number is a warning wherever it sits.
            JudgeHop(current, found);
            var refusal = IpGuard.RefuseUrl(current) ?? await RefuseResolvedAsync(current, ct);
            if (refusal is not null)
            {
                // One message for every refused target, so blocked, internal and unresolvable addresses look the same. The detail stays in the server log.
                _logger?.LogInformation("Link check refused a hop ({Reason}).", refusal);
                chain.Add(new LinkHop("(blocked address)", "blocked"));
                return Bail("It could not be reached.");
            }
            if (!seen.Add(current.AbsoluteUri))
            {
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "blocked"));
                return Bail("The link goes round in a circle.");
            }

            using var hop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hop.CancelAfter(HopTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; BackstopLinkCheck/1.0)");
                request.Headers.TryAddWithoutValidation("Accept", "text/html,*/*;q=0.5");
                using var response = await _client.SendAsync(request, hop.Token);
                var status = (int)response.StatusCode;

                if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    chain.Add(new LinkHop(Shown(current.AbsoluteUri), "redirects"));
                    if (++redirects > MaxRedirects)
                        return Bail("The link sends you on too many times.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "final page"));
                var finalHost = current.IdnHost.ToLowerInvariant();
                var trusted = TrustedDomains.Match(finalHost);

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                var disposition = response.Content.Headers.ContentDisposition?.DispositionType?.ToLowerInvariant();
                var isDownload = ExecutableTypes.Contains(contentType) || disposition == "attachment"
                    || DownloadExtensions.Contains(Path.GetExtension(current.AbsolutePath).ToLowerInvariant());
                if (isDownload)
                    found.High.Add("It starts a download of a program or a packed file.");
                else if (contentType is "" or "text/html" or "application/xhtml+xml")
                {
                    // Read as text and looked through. Never run, never drawn.
                    var text = await ReadCappedAsync(response.Content, hop.Token);
                    // A sign-in box is expected on a well-known site. On any other site it is a warning.
                    if (PasswordBox().IsMatch(text) && trusted is null) found.High.Add("The page asks for a password.");
                }

                // The words shown for the link against the website it really reaches. The only "different website" warning there is.
                if (HasMismatch(shownText, startHost, finalHost, out var mismatch)) found.High.Add(mismatch!);

                if (redirects > 0)
                    found.Info.Add($"It passes through {redirects} other address{(redirects == 1 ? "" : "es")} on the way.");

                // A shortener or a redirect that ends somewhere we do not know is worth a helper's eyes. Ending at a known site is not.
                if (trusted is null && (found.SawShortener || (redirects > 0 && TrustedDomains.Registrable(startHost) != TrustedDomains.Registrable(finalHost))))
                    found.Medium.Add($"It ends at a website we do not recognise: {Shown(current.AbsoluteUri)}");

                if (_reputation is not null)
                {
                    try
                    {
                        var listed = await _reputation.CheckAsync([uri.AbsoluteUri, current.AbsoluteUri], ct);
                        if (listed.Count > 0) found.High.AddRange(listed);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { _logger?.LogWarning("Reputation lookup failed ({Type}). Skipped.", ex.GetType().Name); }
                }

                return Done(shownStart, Shown(current.AbsoluteUri), chain, found, verified: true, fetched: true, trusted,
                    summary: null, unsafeIfUnverified: true);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "no answer"));
                return Bail("It took too long to answer.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or SocketException or InvalidOperationException)
            {
                // The detail can hold the address, so only the kind of error is logged.
                _logger?.LogInformation("Link check could not open a link ({Type}).", ex.GetType().Name);
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "no answer"));
                return Bail("It could not be reached.");
            }
        }
    }

    // What one address gives away on its own, sorted into the three tiers.
    private static void JudgeHop(Uri hop, Found found)
    {
        if (hop.Scheme != Uri.UriSchemeHttps) found.High.Add($"A step on the way is not secure: {Shown(hop.AbsoluteUri)}");
        foreach (var note in LinkFinder.Judge(hop.AbsoluteUri, MaxJudgedLength).Signals)
        {
            switch (note)
            {
                case LinkFinder.NotSecureNote: break; // said above, once, for every hop
                case LinkFinder.ShortenerNote:
                    found.SawShortener = true;
                    found.Info.Add("It is a shortened link, so Backstop followed it to see where it goes.");
                    break;
                case LinkFinder.RiskyWordsNote:
                case LinkFinder.VeryLongNote:
                    found.Medium.Add(note);
                    break;
                default: found.High.Add(note); break;
            }
        }
    }

    // Looks the name up now and refuses it if ANY answer is not a public address. The connect step looks it up and checks it again before dialling.
    private async Task<string?> RefuseResolvedAsync(Uri uri, CancellationToken ct)
    {
        var host = uri.IdnHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out _)) return null; // a literal was already judged in RefuseUrl
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HopTimeout);
            var addresses = await _resolve(host, cts.Token);
            if (addresses.Length == 0) return "The website name does not exist.";
            return addresses.All(IpGuard.IsPublic) ? null : "The address points to a private or internal place.";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "It took too long to answer."; }
        catch (Exception ex) when (ex is SocketException or ArgumentException or IOException) { return "The website name does not exist."; }
    }

    private static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        while (ms.Length < MaxBodyBytes)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaxBodyBytes - ms.Length)), ct);
            if (n == 0) break;
            ms.Write(buffer, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static LinkReport Fail(string start, List<LinkHop> chain, Found found, string why)
    {
        found.High.Add(why);
        return Done(start, null, chain, found, verified: false, fetched: false, trusted: null, "Could not verify, treated as unsafe.", unsafeIfUnverified: true);
    }

    // High is dangerous, Medium is "be careful" (held), Info never counts. A well-known final site with no High signal is fine,
    // and then even a Medium note (a sign-in word in the address) is only information.
    private static LinkReport Done(string start, string? final, List<LinkHop> chain, Found found, bool verified, bool fetched,
        string? trusted, string? summary, bool unsafeIfUnverified)
    {
        var high = found.High.Distinct().ToList();
        var medium = found.Medium.Distinct().ToList();
        var info = found.Info.Distinct().ToList();
        var knownSite = verified && trusted is not null && high.Count == 0;
        if (knownSite) { info.AddRange(medium); medium.Clear(); }

        var verdict = high.Count > 0 || (!verified && unsafeIfUnverified) ? LinkVerdict.Unsafe : medium.Count > 0 ? LinkVerdict.Suspicious : LinkVerdict.NoWarnings;
        summary ??= knownSite ? $"Looks fine: goes to {trusted}."
            : verdict == LinkVerdict.Unsafe ? "Looks dangerous."
            : verdict == LinkVerdict.Suspicious ? "Be careful with this one."
            : "No warning signs found.";
        return new LinkReport(start, final, chain, high.Concat(medium).ToList(), verdict, verified, fetched, summary, info, knownSite ? trusted : null);
    }
    private static string Shown(string url) => Defang.Url(url.Length > LinkFinder.MaxLength ? url[..LinkFinder.MaxLength] : url);

    // "mybank.example.com" shown, "evil.example.net" reached. Compared as names, so www. and sub-sites are not a mismatch.
    private static bool HasMismatch(string? shownText, string startHost, string? finalHost, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(shownText)) return false;
        var m = LinkFinder.Pattern.Match(shownText);
        if (!m.Success) return false;
        var shownRaw = m.Value;
        var candidate = shownRaw.Contains("://") ? shownRaw : "http://" + shownRaw;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var shownUri)) return false;
        var shownHost = shownUri.IdnHost.ToLowerInvariant();
        var goesTo = finalHost ?? startHost;
        // Words that honestly name the link itself (a shortener written out) are not a trick.
        if (SameName(shownHost, goesTo) || SameName(shownHost, startHost)) return false;
        message = $"The link says {Defang.Url(shownRaw)} but it goes to {Defang.Url(goesTo)}.";
        return true;
    }

    private static bool SameName(string a, string b)
    {
        a = a.StartsWith("www.") ? a[4..] : a;
        b = b.StartsWith("www.") ? b[4..] : b;
        return a == b || a.EndsWith("." + b) || b.EndsWith("." + a);
    }

    private static readonly HashSet<string> ExecutableTypes =
    [
        "application/x-msdownload", "application/x-dosexec", "application/vnd.microsoft.portable-executable", "application/x-msi",
        "application/octet-stream", "application/zip", "application/x-zip-compressed", "application/x-7z-compressed",
        "application/x-rar-compressed", "application/vnd.rar", "application/x-tar", "application/gzip", "application/java-archive",
        "application/vnd.android.package-archive", "application/x-apple-diskimage", "application/x-sh", "application/x-bat",
    ];

    private static readonly HashSet<string> DownloadExtensions =
    [
        ".exe", ".msi", ".scr", ".bat", ".cmd", ".com", ".pif", ".lnk", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".ps1", ".jar",
        ".apk", ".dmg", ".pkg", ".iso", ".img", ".zip", ".rar", ".7z", ".tar", ".gz", ".cab", ".docm", ".xlsm",
    ];

    [GeneratedRegex("<input\\b[^>]*\\btype\\s*=\\s*[\"']?password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordBox();

    // ---- Finding the links in a message, with the words shown for each one ----

    [GeneratedRegex("<a\\s[^>]*\\bhref\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Anchor();

    [GeneratedRegex("\\[([^\\]\\r\\n]{1,200})\\]\\((https?://[^)\\s]+)\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Markdown();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    /// <summary>Every distinct link in the text, with the words shown for it when the message wrote it as a named link.</summary>
    public static IReadOnlyList<(string Url, string? Shown)> ExtractLinks(string? text)
    {
        var list = new List<(string, string?)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url, string? shown)
        {
            url = url.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
            if (url.Length > 0 && seen.Add(url)) list.Add((url, shown));
        }
        text ??= "";
        foreach (Match m in Anchor().Matches(text)) Add(m.Groups[1].Value, Tags().Replace(m.Groups[2].Value, "").Trim());
        foreach (Match m in Markdown().Matches(text)) Add(m.Groups[2].Value, m.Groups[1].Value);
        // Named links are taken out first, so the words shown for them are not mistaken for links of their own.
        var rest = Markdown().Replace(Anchor().Replace(text, " "), " ");
        foreach (Match m in LinkFinder.Pattern.Matches(rest)) Add(m.Value, null);
        return list;
    }
}
