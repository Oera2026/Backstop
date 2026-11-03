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
/// is pinned to the address that passed (see <see cref="CreateClient"/>), so the name cannot change between the check and the visit.
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

    public async Task<LinkReport> InspectAsync(string url, string? shownText = null, CancellationToken ct = default)
    {
        var raw = (url ?? "").Trim();
        var start = raw.Contains("://") ? raw : "https://" + raw;
        var shownStart = Shown(raw);
        var signals = new List<string>();
        var chain = new List<LinkHop>();
        var strong = false;
        var warn = false;

        // What the address alone gives away. Same rules as real mail.
        var (addressSignals, problem, disguised) = LinkFinder.Judge(raw);
        if (problem) { signals.AddRange(addressSignals); warn = true; strong |= disguised; }

        if (!Uri.TryCreate(start, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return Fail(shownStart, chain, signals, "The address is not a proper web address.");

        var startHost = uri.IdnHost.ToLowerInvariant();
        if (HasMismatch(shownText, startHost, null, out var mismatch)) { signals.Add(mismatch!); strong = true; warn = true; }

        if (SkipReservedExamples && IpGuard.IsReservedExample(startHost))
        {
            chain.Add(new LinkHop(Shown(uri.AbsoluteUri), "not opened"));
            return Done(shownStart, null, chain, signals, strong, warn, verified: false, fetched: false,
                "Example address: only the address was checked.", unsafeIfUnverified: false);
        }

        var current = uri;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redirects = 0;
        while (true)
        {
            var refusal = IpGuard.RefuseUrl(current) ?? await RefuseResolvedAsync(current, ct);
            if (refusal is not null)
            {
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "blocked"));
                return Fail(shownStart, chain, signals, refusal, strong, warn);
            }
            if (!seen.Add(current.AbsoluteUri))
            {
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "blocked"));
                return Fail(shownStart, chain, signals, "The link goes round in a circle.", strong, warn);
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
                        return Fail(shownStart, chain, signals, "The link sends you on too many times.", strong, warn);
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "final page"));
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                var disposition = response.Content.Headers.ContentDisposition?.DispositionType?.ToLowerInvariant();
                var isDownload = ExecutableTypes.Contains(contentType) || disposition == "attachment"
                    || DownloadExtensions.Contains(Path.GetExtension(current.AbsolutePath).ToLowerInvariant());
                if (isDownload)
                {
                    signals.Add("It starts a download of a program or a packed file.");
                    strong = true; warn = true;
                }
                else if (contentType is "" or "text/html" or "application/xhtml+xml")
                {
                    // Read as text and looked through. Never run, never drawn.
                    var text = await ReadCappedAsync(response.Content, hop.Token);
                    if (PasswordBox().IsMatch(text)) { signals.Add("The page asks for a password."); warn = true; }
                }

                var finalHost = current.IdnHost.ToLowerInvariant();
                if (redirects > 0 && !SameSite(startHost, finalHost))
                {
                    signals.Add($"It sends you to a different website: {Shown(current.AbsoluteUri)}");
                    warn = true;
                }
                if (redirects > 0)
                {
                    var (finalSignals, finalProblem, finalDisguised) = LinkFinder.Judge(current.AbsoluteUri);
                    if (finalProblem)
                    {
                        foreach (var s in finalSignals) if (!signals.Contains(s)) signals.Add(s);
                        warn = true; strong |= finalDisguised;
                    }
                    if (HasMismatch(shownText, startHost, finalHost, out var m2) && !signals.Contains(m2!)) { signals.Add(m2!); strong = true; warn = true; }
                }

                if (_reputation is not null)
                {
                    try
                    {
                        var listed = await _reputation.CheckAsync([uri.AbsoluteUri, current.AbsoluteUri], ct);
                        if (listed.Count > 0) { signals.AddRange(listed); strong = true; warn = true; }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { _logger?.LogWarning("Reputation lookup failed ({Type}). Skipped.", ex.GetType().Name); }
                }

                return Done(shownStart, Shown(current.AbsoluteUri), chain, signals, strong, warn, verified: true, fetched: true,
                    warn ? (strong ? "Looks dangerous." : "Be careful with this one.") : "No warning signs found.", unsafeIfUnverified: true);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "no answer"));
                return Fail(shownStart, chain, signals, "It took too long to answer.", strong, warn);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or SocketException or InvalidOperationException)
            {
                // The detail can hold the address, so only the kind of error is logged.
                _logger?.LogInformation("Link check could not open a link ({Type}).", ex.GetType().Name);
                chain.Add(new LinkHop(Shown(current.AbsoluteUri), "no answer"));
                return Fail(shownStart, chain, signals, "It could not be reached.", strong, warn);
            }
        }
    }

    // Looks the name up now and refuses it if ANY answer is not a public address. The connection is pinned again at connect time.
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

    private static LinkReport Fail(string start, List<LinkHop> chain, List<string> signals, string why, bool strong = false, bool warn = false)
    {
        signals.Add(why);
        return Done(start, null, chain, signals, strong, warn, verified: false, fetched: false, "Could not verify, treated as unsafe.", unsafeIfUnverified: true);
    }

    private static LinkReport Done(string start, string? final, List<LinkHop> chain, List<string> signals, bool strong, bool warn,
        bool verified, bool fetched, string summary, bool unsafeIfUnverified)
    {
        var verdict = strong || (!verified && unsafeIfUnverified) ? LinkVerdict.Unsafe : warn ? LinkVerdict.Suspicious : LinkVerdict.NoWarnings;
        return new LinkReport(start, final, chain, signals.Distinct().ToList(), verdict, verified, fetched, summary);
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

    private static bool SameSite(string a, string b)
    {
        static string Tail(string h) { var p = h.Split('.'); return p.Length <= 2 ? h : string.Join('.', p[^2..]); }
        return SameName(a, b) || Tail(a) == Tail(b);
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
