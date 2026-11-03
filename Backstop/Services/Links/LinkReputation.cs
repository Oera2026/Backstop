using System.Text;
using System.Text.Json;

namespace Backstop.Services.Links;

/// <summary>Optional lookups of a link address in two public blocklists. Only used from the Try it page.</summary>
public interface ILinkReputation
{
    /// <summary>Plain sentences for every list that names one of the addresses. Empty when none does, when no key is set, or when a lookup failed.</summary>
    Task<IReadOnlyList<string>> CheckAsync(IReadOnlyList<string> urls, CancellationToken ct = default);
}

/// <summary>
/// Google Safe Browsing (SAFE_BROWSING_API_KEY) and URLhaus (URLHAUS_AUTH_KEY). Each is skipped silently when its key is empty.
/// Only the link address is sent, never the message. A lookup that fails adds nothing: these lists only ever make a result stricter.
/// The hosts are fixed here. The keys go in headers, so they never appear in a logged address.
/// </summary>
public sealed class LinkReputation(HttpClient http, IConfiguration config, ILogger<LinkReputation> logger) : ILinkReputation
{
    internal const string SafeBrowsingUrl = "https://safebrowsing.googleapis.com/v4/threatMatches:find";
    internal const string UrlhausUrl = "https://urlhaus-api.abuse.ch/v1/url/";

    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyList<string> urls, CancellationToken ct = default)
    {
        var list = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().Take(2).ToList();
        var found = new List<string>();
        if (list.Count == 0) return found;

        var sb = config["SAFE_BROWSING_API_KEY"];
        var uh = config["URLHAUS_AUTH_KEY"];
        if (!string.IsNullOrWhiteSpace(sb) && await SafeBrowsingAsync(list, sb.Trim(), ct))
            found.Add("Google Safe Browsing lists this address as dangerous.");
        if (!string.IsNullOrWhiteSpace(uh) && await UrlhausAsync(list, uh.Trim(), ct))
            found.Add("URLhaus lists this address as a place that spreads harmful software.");
        return found;
    }

    private async Task<bool> SafeBrowsingAsync(List<string> urls, string key, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                client = new { clientId = "backstop", clientVersion = "1.0" },
                threatInfo = new
                {
                    threatTypes = new[] { "MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION" },
                    platformTypes = new[] { "ANY_PLATFORM" },
                    threatEntryTypes = new[] { "URL" },
                    threatEntries = urls.Select(u => new { url = u }).ToArray(),
                },
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, SafeBrowsingUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Goog-Api-Key", key);
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) { logger.LogWarning("Safe Browsing lookup answered {Status}. Skipped.", (int)res.StatusCode); return false; }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("matches", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Safe Browsing lookup failed ({Type}). Skipped.", ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> UrlhausAsync(List<string> urls, string key, CancellationToken ct)
    {
        foreach (var url in urls)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, UrlhausUrl) { Content = new FormUrlEncodedContent([new("url", url)]) };
                req.Headers.Add("Auth-Key", key);
                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) { logger.LogWarning("URLhaus lookup answered {Status}. Skipped.", (int)res.StatusCode); continue; }
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("query_status", out var s) && s.GetString() == "ok") return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("URLhaus lookup failed ({Type}). Skipped.", ex.GetType().Name);
            }
        }
        return false;
    }
}
