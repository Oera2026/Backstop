namespace Backstop.Services.Links;

/// <summary>
/// Well-known websites a link may safely end at. Used only by the Link check on /try, and only for the FINAL address after every
/// hop has been followed and checked. A name matches when it is exactly one of these, or a true subdomain of one (a dot before it),
/// so "amazon.co.uk.evil.example.net" and "notamazon.co.uk" never match. This is a list of names to compare, not addresses to visit.
/// </summary>
public static class TrustedDomains
{
    public static readonly IReadOnlyList<string> All =
    [
        // Shops and delivery
        "amazon.co.uk", "amazon.com", "ebay.co.uk", "argos.co.uk", "tesco.com", "sainsburys.co.uk", "asda.com", "johnlewis.com",
        "royalmail.com", "dpd.co.uk", "evri.com",
        // Government and health
        "gov.uk", "nhs.uk", "hmrc.gov.uk", "dvla.gov.uk",
        // Banks and payments
        "barclays.co.uk", "lloydsbank.com", "hsbc.co.uk", "natwest.com", "santander.co.uk", "nationwide.co.uk", "halifax.co.uk",
        "tsb.co.uk", "monzo.com", "starlingbank.com", "paypal.com", "paypal.me", "visa.com", "mastercard.com",
        // Technology
        "microsoft.com", "live.com", "office.com", "apple.com", "icloud.com", "google.com", "youtube.com", "android.com",
        "dropbox.com", "zoom.us", "whatsapp.com", "facebook.com", "linkedin.com",
        // Everyday services
        "bbc.co.uk", "bbc.com", "wikipedia.org", "bt.com", "sky.com", "virginmedia.com", "ee.co.uk", "vodafone.co.uk", "o2.co.uk",
    ];

    // Pages anyone can publish on. A name under a trusted domain, but not a trusted place.
    private static readonly string[] UserContent =
    [
        "sites.google.com", "docs.google.com", "drive.google.com", "forms.google.com", "storage.googleapis.com", "script.google.com",
        "groups.google.com", "sway.office.com", "forms.office.com", "1drv.ms", "onedrive.live.com",
    ];

    /// <summary>The trusted domain this host is, or sits under. Null when it is not on the list.</summary>
    public static string? Match(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        // Look-alike letters never match, and a literal address is not a name.
        if (host.Contains("xn--") || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _)) return null;
        if (UserContent.Any(u => host == u || host.EndsWith("." + u, StringComparison.Ordinal))) return null;
        foreach (var d in All)
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) return d;
        return null;
    }

    /// <summary>The part of a name a site owner registers (shop.example.co.uk gives example.co.uk). Good enough for "same site".</summary>
    public static string Registrable(string host)
    {
        var parts = host.Trim().TrimEnd('.').ToLowerInvariant().Split('.');
        if (parts.Length <= 2) return string.Join('.', parts);
        var tail = parts[^2] + "." + parts[^1];
        var take = SecondLevel.Contains(tail) ? 3 : 2;
        return string.Join('.', parts[^Math.Min(take, parts.Length)..]);
    }

    private static readonly HashSet<string> SecondLevel = ["co.uk", "org.uk", "gov.uk", "ac.uk", "me.uk", "ltd.uk", "plc.uk", "nhs.uk", "com.au", "co.nz", "co.za"];
}
