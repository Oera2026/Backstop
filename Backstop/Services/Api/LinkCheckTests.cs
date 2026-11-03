#if DEBUG
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Backstop.Models;
using Backstop.Services.Links;

namespace Backstop.Tests;

/// <summary>
/// Link check tests. Nothing here touches the network: DNS and the web are faked. Called from LlamaScamCheckerTests.RunAllTests.
/// </summary>
public static class LinkCheckTests
{
    private sealed class FakeWeb(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return handler(request, ct);
        }
    }

    private sealed class CountingStream : Stream
    {
        public long Read_;
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)'a', offset, count); Read_ += count; return count; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubReputation(Func<IReadOnlyList<string>> answer) : ILinkReputation
    {
        public Task<IReadOnlyList<string>> CheckAsync(IReadOnlyList<string> urls, CancellationToken ct = default) => Task.FromResult(answer());
    }

    private static HttpResponseMessage Redirect(string to, int status = 302) =>
        new((HttpStatusCode)status) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

    private static HttpResponseMessage Html(string body, string type = "text/html") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    private static (LinkInspector Inspector, FakeWeb Web) Make(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Dictionary<string, string[]>? dns = null, ILinkReputation? reputation = null, TimeSpan? timeout = null)
    {
        var web = new FakeWeb(handler);
        Task<IPAddress[]> Resolve(string host, CancellationToken _) =>
            Task.FromResult((dns is not null && dns.TryGetValue(host, out var a) ? a : ["93.184.216.34"]).Select(IPAddress.Parse).ToArray());
        return (new LinkInspector(new HttpMessageInvoker(web), Resolve, reputation) { SkipReservedExamples = false, HopTimeout = timeout ?? TimeSpan.FromSeconds(3) }, web);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    public static async Task RunAsync()
    {
        // ---- Which addresses may be opened ----
        foreach (var bad in new[]
        {
            "10.1.2.3", "10.255.255.255", "172.16.0.1", "172.31.255.255", "192.168.1.1", "127.0.0.1", "127.8.8.8", "169.254.169.254", "169.254.0.1",
            "0.0.0.0", "100.64.0.1", "224.0.0.1", "255.255.255.255", "192.0.2.45", "::1", "::", "fc00::1", "fd12:3456::1", "fd00:ec2::254",
            "fe80::1", "ff02::1", "::ffff:127.0.0.1", "::ffff:10.0.0.1", "::ffff:169.254.169.254", "64:ff9b::7f00:1", "2002:7f00:1::1", "2001:db8::1",
        })
            Assert(!IpGuard.IsPublic(IPAddress.Parse(bad)), $"Blocked address: {bad}");
        foreach (var good in new[] { "8.8.8.8", "93.184.216.34", "172.32.0.1", "172.15.255.255", "11.0.0.1", "2606:4700:4700::1111" })
            Assert(IpGuard.IsPublic(IPAddress.Parse(good)), $"Public address allowed: {good}");

        // ---- Which web addresses may be opened ----
        foreach (var bad in new[]
        {
            "http://localhost/", "http://LOCALHOST./", "http://app.localhost/", "http://127.0.0.1/", "http://[::1]/", "http://169.254.169.254/latest/meta-data/",
            "http://metadata.google.internal/", "http://intranet/", "http://printer.local/", "http://example.com:8080/", "https://example.com:444/",
            "ftp://example.com/", "file:///etc/passwd", "http://user:pw@example.com/", "http://2130706433/", "http://10.0.0.1/",
        })
            Assert(IpGuard.RefuseUrl(new Uri(bad)) is not null, $"Refused: {bad}");
        foreach (var good in new[] { "https://example.com/", "http://example.com:80/", "https://example.com:443/x?y=1" })
            Assert(IpGuard.RefuseUrl(new Uri(good)) is null, $"Allowed: {good}");
        Assert(IpGuard.IsReservedExample("shop.example.net") && IpGuard.IsReservedExample("a.test") && !IpGuard.IsReservedExample("example.co.uk"), "Reserved example names");
        Console.WriteLine("PASS: Private, loopback, link-local, metadata and odd-port addresses are refused.");

        // ---- Refused before anything is sent ----
        {
            var (insp, web) = Make((_, _) => Task.FromResult(Html("hi")), new() { ["internal.example.com"] = ["10.0.0.5"], ["mixed.example.com"] = ["93.184.216.34", "192.168.1.5"], ["gone.example.com"] = [] });
            foreach (var url in new[] { "https://internal.example.com/a", "https://mixed.example.com/a", "https://gone.example.com/a", "http://169.254.169.254/latest/meta-data/",
                                        "http://localhost/admin", "http://127.0.0.1:8080/", "http://[::1]/", "https://example.com:8443/" })
            {
                var r = await insp.InspectAsync(url);
                Assert(r.Verdict == LinkVerdict.Unsafe && !r.Verified && !r.Fetched && r.Summary.StartsWith("Could not verify, treated as unsafe"), $"Refused and unsafe: {url}");
            }
            Assert(web.Requests == 0, "A refused address sends nothing");
        }

        // ---- A redirect to a private place is refused on that hop ----
        {
            var (insp, web) = Make((req, _) => Task.FromResult(req.RequestUri!.Host switch
            {
                "start.example.com" => Redirect("http://intranet.example.com/admin"),
                "hop.example.com" => Redirect("http://127.0.0.1/"),
                "meta.example.com" => Redirect("http://169.254.169.254/latest/meta-data/"),
                _ => Html("hi"),
            }), new() { ["intranet.example.com"] = ["192.168.0.9"] });
            foreach (var host in new[] { "start", "hop", "meta" })
            {
                var before = web.Requests;
                var r = await insp.InspectAsync($"https://{host}.example.com/x");
                Assert(r.Verdict == LinkVerdict.Unsafe && !r.Verified && r.Chain[^1].Note == "blocked", $"Redirect to a private place is blocked ({host})");
                Assert(web.Requests - before == 1, $"Only the first hop was ever sent ({host})");
            }
        }

        // ---- Redirect loop and endless chains are capped ----
        {
            var (insp, web) = Make((req, _) => Task.FromResult(Redirect(req.RequestUri!.AbsolutePath == "/a" ? "https://loop.example.com/b" : "https://loop.example.com/a")));
            var loop = await insp.InspectAsync("https://loop.example.com/a");
            Assert(!loop.Verified && loop.Verdict == LinkVerdict.Unsafe && loop.Signals.Any(s => s.Contains("circle")) && web.Requests <= 3, "A redirect loop stops quickly and is unsafe");

            var (insp2, web2) = Make((req, _) => Task.FromResult(Redirect("https://long.example.com/" + (web2Count++))));
            var endless = await insp2.InspectAsync("https://long.example.com/start");
            Assert(!endless.Verified && endless.Verdict == LinkVerdict.Unsafe && web2.Requests == LinkInspector.MaxRedirects + 1, "A chain of new addresses stops after 5 redirects");

        }

        // ---- Exactly five redirects is allowed, and the chain is reported ----
        {
            var (insp, _) = Make((req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                var n = path == "/" ? 0 : int.Parse(path[1..]);
                return Task.FromResult(n < 5 ? Redirect("/" + (n + 1)) : Html("<p>welcome</p>"));
            });
            var r = await insp.InspectAsync("https://five.example.com/");
            Assert(r.Verified && r.Fetched && r.Chain.Count == 6 && r.Chain[0].Note == "redirects" && r.Chain[^1].Note == "final page" && r.FinalUrl!.EndsWith("/5"), "Five redirects are followed and the chain is listed");
            Assert(r.Verdict == LinkVerdict.NoWarnings, "A clean page has no warnings");
        }

        // ---- Only a plain GET: no cookies, no sign-in headers, no referrer ----
        {
            HttpRequestMessage? seen = null;
            var (insp, _) = Make((req, _) => { seen = req; return Task.FromResult(Html("hi")); });
            await insp.InspectAsync("https://plain.example.com/page");
            Assert(seen is not null && seen.Method == HttpMethod.Get && seen.Content is null
                && seen.Headers.Authorization is null && !seen.Headers.Contains("Cookie") && seen.Headers.Referrer is null, "GET only, no cookies, no auth, no referrer");
        }

        // ---- Downloads ----
        {
            var (insp, _) = Make((req, _) => Task.FromResult(req.RequestUri!.AbsolutePath switch
            {
                "/setup" => Html("MZ...", "application/x-msdownload"),
                "/packed" => Html("PK...", "application/zip"),
                "/blob" => Html("..", "application/octet-stream"),
                "/get" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x", Encoding.UTF8, "text/plain"), Headers = { } }.WithAttachment(),
                _ => Html("<p>fine</p>", "text/html"),
            }));
            foreach (var path in new[] { "/setup", "/packed", "/blob", "/get", "/invoice.exe", "/files/update.msi" })
            {
                var r = await insp.InspectAsync("https://dl.example.com" + path);
                Assert(r.Verdict == LinkVerdict.Unsafe && r.Verified && r.Signals.Any(s => s.Contains("download")), $"Download detected: {path}");
            }
            Assert((await insp.InspectAsync("https://dl.example.com/page")).Verdict == LinkVerdict.NoWarnings, "An ordinary page is not a download");
        }

        // ---- A login page with a password box ----
        {
            var (insp, _) = Make((_, _) => Task.FromResult(Html("<form><input name=u><INPUT TYPE='password' name=p></form>")));
            var r = await insp.InspectAsync("https://bank.example.com/");
            Assert(r.Verified && r.Verdict == LinkVerdict.Suspicious && r.Signals.Contains("The page asks for a password."), "A password box is flagged");
        }

        // ---- The page is read up to 1 MB and no further ----
        {
            var stream = new CountingStream();
            var (insp, _) = Make((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) { Headers = { ContentType = new("text/html") } } }));
            var r = await insp.InspectAsync("https://huge.example.com/");
            Assert(r.Verified && stream.Read_ <= LinkInspector.MaxBodyBytes + 8192, $"Read at most 1 MB ({stream.Read_} bytes)");

            var late = new string('a', LinkInspector.MaxBodyBytes + 100) + "<input type=\"password\">";
            var (insp2, _) = Make((_, _) => Task.FromResult(Html(late)));
            Assert(!(await insp2.InspectAsync("https://late.example.com/")).Signals.Any(s => s.Contains("password")), "Nothing past the 1 MB cap is looked at");
        }

        // ---- Timeouts and errors fail closed ----
        {
            var (slow, _) = Make(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Html("never"); }, timeout: TimeSpan.FromMilliseconds(200));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = await slow.InspectAsync("https://slow.example.com/");
            Assert(sw.Elapsed < TimeSpan.FromSeconds(3) && !t.Verified && t.Verdict == LinkVerdict.Unsafe && t.Summary.StartsWith("Could not verify, treated as unsafe") && t.Signals.Any(s => s.Contains("too long")), "A slow answer times out and is unsafe");

            var (slowBody, _) = Make((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new HangingStream()) { Headers = { ContentType = new("text/html") } },
            }), timeout: TimeSpan.FromMilliseconds(200));
            Assert(!(await slowBody.InspectAsync("https://drip.example.com/")).Verified, "A page that never finishes times out too");

            var (broken, _) = Make((_, _) => throw new HttpRequestException("connection reset"));
            var b = await broken.InspectAsync("https://broken.example.com/");
            Assert(!b.Verified && b.Verdict == LinkVerdict.Unsafe, "A network error is unsafe");
            Assert(!(await broken.InspectAsync("not a web address at all ::")).Verified, "Junk is unsafe");
        }

        // ---- The signals ----
        {
            var (insp, _) = Make((_, _) => Task.FromResult(Html("<p>hi</p>")));
            var raw = await insp.InspectAsync("http://203.0.113.9/login");
            Assert(raw.Verdict == LinkVerdict.Unsafe && raw.Signals.Any(s => s.Contains("number instead of a website name")), "A raw IP address is flagged");
            var puny = await insp.InspectAsync("https://xn--pypal-4ve.example.com/");
            Assert(puny.Verdict == LinkVerdict.Unsafe && puny.Signals.Any(s => s.Contains("look-alike")), "Look-alike letters are flagged");
            var shortener = await insp.InspectAsync("https://bit.ly/abc");
            Assert(shortener.Verdict == LinkVerdict.Unsafe && shortener.Signals.Any(s => s.Contains("shortened")), "A shortener is flagged");
            var mismatch = await insp.InspectAsync("https://evil.example.net/x", "www.mybank.example.com");
            Assert(mismatch.Verdict == LinkVerdict.Unsafe && mismatch.Signals.Any(s => s.Contains("says") && s.Contains("[.]")), "Link text that names another site is flagged, defanged");
            var same = await insp.InspectAsync("https://www.mybank.example.com/x", "mybank.example.com");
            Assert(same.Verdict == LinkVerdict.NoWarnings, "The same site with or without www is not a mismatch");

            var (hop, _) = Make((req, _) => Task.FromResult(req.RequestUri!.Host == "go.example.com" ? Redirect("https://other.example.net/landing") : Html("<p>x</p>")));
            var moved = await hop.InspectAsync("https://go.example.com/a");
            Assert(moved.Verdict == LinkVerdict.Suspicious && moved.Signals.Any(s => s.Contains("different website")) && moved.FinalUrl == "hxxps://other[.]example[.]net/landing", "A redirect to another website is reported, defanged");

            // Optional blocklists: a hit makes it unsafe; a failure or silence changes nothing.
            var (listed, _) = Make((_, _) => Task.FromResult(Html("<p>x</p>")), reputation: new StubReputation(() => ["Google Safe Browsing lists this address as dangerous."]));
            Assert((await listed.InspectAsync("https://flagged.example.com/")).Verdict == LinkVerdict.Unsafe, "A blocklist hit is unsafe");
            var (quiet, _) = Make((_, _) => Task.FromResult(Html("<p>x</p>")), reputation: new StubReputation(() => []));
            Assert((await quiet.InspectAsync("https://clean.example.com/")).Verdict == LinkVerdict.NoWarnings, "No blocklist hit changes nothing");
            var (failing, _) = Make((_, _) => Task.FromResult(Html("<p>x</p>")), reputation: new StubReputation(() => throw new HttpRequestException("down")));
            Assert((await failing.InspectAsync("https://clean.example.com/")).Verdict == LinkVerdict.NoWarnings, "A failing blocklist lookup is skipped silently");
        }

        // ---- No keys: the blocklist lookups do nothing and send nothing ----
        {
            var sent = 0;
            var http = new HttpClient(new FakeWeb((_, _) => { sent++; return Task.FromResult(Html("{}")); }));
            var rep = new LinkReputation(http, new ConfigurationBuilder().Build(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LinkReputation>.Instance);
            Assert((await rep.CheckAsync(["https://x.example.com/"])).Count == 0 && sent == 0, "No keys: skipped silently, nothing sent");
        }

        // ---- Reserved example addresses are judged by address only and never opened ----
        {
            var web = new FakeWeb((_, _) => throw new InvalidOperationException("must not be opened"));
            var insp = new LinkInspector(new HttpMessageInvoker(web));
            var shop = await insp.InspectAsync("https://shop.example.net/book");
            Assert(web.Requests == 0 && !shop.Fetched && shop.Verdict == LinkVerdict.NoWarnings && shop.Summary.Contains("Example address"), "An example address is not opened");
            Assert((await insp.InspectAsync("http://192.0.2.45/track?id=88")).Verdict == LinkVerdict.Unsafe && web.Requests == 0, "A documentation IP is unsafe and not opened");
            Assert((await insp.InspectAsync("https://bit.ly/x")).Verdict == LinkVerdict.Unsafe, "A shortener stays unsafe");
        }

        // ---- Finding links, with the words shown for each ----
        {
            var found = LinkInspector.ExtractLinks("Click <a href=\"https://evil.example.net/x\">www.mybank.com</a> or [my bank](https://bad.example.org/y) or https://plain.example.com/z, and bit.ly/abc.");
            Assert(found.Count == 4 && found[0].Shown == "www.mybank.com" && found[1].Shown == "my bank" && found.Skip(2).All(f => f.Shown is null), "Named links keep their words; plain links are found once");
        }
        Console.WriteLine("PASS: Link check follows redirects safely, caps them, spots downloads, fails closed on timeouts and errors.");
    }

    private static int web2Count;

    private static HttpResponseMessage WithAttachment(this HttpResponseMessage m)
    {
        m.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "report.txt" };
        return m;
    }

    private sealed class HangingStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
#endif
