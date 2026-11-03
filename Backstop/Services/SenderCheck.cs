using System.Globalization;
using System.Text.RegularExpressions;
using Backstop.Models;

namespace Backstop.Services;

/// <summary>
/// Looks at WHO a message says it is from: the name shown, the address, and the Reply-To. Pure code, no AI call, no network.
/// It returns evidence only. The policy (MailAnalyzer.Decide) turns a High signal into "held for a helper" and nothing here
/// can ever make a result looser.
/// </summary>
public interface ISenderCheck
{
    /// <summary>
    /// Signals for this sender, most serious first; empty when nothing looks wrong. Never throws: if the headers cannot be read
    /// the answer is a High "unverified-sender" signal, so the message is held.
    /// </summary>
    IReadOnlyList<SenderSignal> Check(InboundMail mail, IReadOnlyList<Contact> contacts);
}

public sealed partial class SenderCheck : ISenderCheck
{
    public static SenderCheck Default { get; } = new();

    // --- Reference data. These are detection lists, not message data. ---

    private sealed record Brand(string Name, string[] Aliases, string[] Domains)
    {
        /// <summary>The name part of the first domain, e.g. "paypal" for paypal.com. Lookalikes are measured against this.</summary>
        public string Label => _label ??= Split(Domains[0]).Label;
        private string? _label;
    }

    // A brand's own domains. A host that is one of these, or a subdomain of one, is the real thing.
    // "northbank" is a made-up stand-in so the demo can show the lookalike checks with a .test address only.
    private static readonly Brand[] Brands =
    [
        new("PayPal", ["paypal"], ["paypal.com", "paypal.co.uk", "paypal.me"]),
        new("Amazon", ["amazon", "amazon prime"], ["amazon.com", "amazon.co.uk", "amazon.de", "amazon.fr", "amazon.es", "amazon.it"]),
        new("Microsoft", ["microsoft", "windows", "office 365", "xbox"], ["microsoft.com", "office.com", "office365.com", "microsoftonline.com", "xbox.com"]),
        new("Apple", ["apple", "apple id", "itunes", "app store"], ["apple.com", "itunes.com"]),
        new("Google", ["google"], ["google.com", "google.co.uk"]),
        new("Netflix", ["netflix"], ["netflix.com"]),
        new("eBay", ["ebay"], ["ebay.com", "ebay.co.uk"]),
        new("Facebook", ["facebook"], ["facebook.com", "facebookmail.com", "meta.com"]),
        new("Instagram", ["instagram"], ["instagram.com"]),
        new("WhatsApp", ["whatsapp"], ["whatsapp.com"]),
        new("Royal Mail", ["royal mail", "royalmail"], ["royalmail.com"]),
        new("Evri", ["evri", "hermes"], ["evri.com"]),
        new("DPD", ["dpd"], ["dpd.co.uk", "dpd.com"]),
        new("UPS", ["ups"], ["ups.com"]),
        new("FedEx", ["fedex"], ["fedex.com"]),
        new("DHL", ["dhl"], ["dhl.com", "dhl.co.uk", "dhl.de"]),
        new("Parcelforce", ["parcelforce"], ["parcelforce.com"]),
        new("Post Office", ["post office"], ["postoffice.co.uk"]),
        new("HMRC", ["hmrc", "hm revenue and customs", "hm revenue customs"], ["hmrc.gov.uk"]),
        new("DVLA", ["dvla"], ["dvla.gov.uk"]),
        new("The NHS", ["nhs", "nhs england", "nhs test and trace"], ["nhs.uk", "nhs.net"]),
        new("TV Licensing", ["tv licensing", "tv licence", "tv license"], ["tvlicensing.co.uk"]),
        new("Barclays", ["barclays"], ["barclays.co.uk", "barclays.com"]),
        new("Lloyds Bank", ["lloyds", "lloyds bank"], ["lloydsbank.com", "lloydsbank.co.uk"]),
        new("HSBC", ["hsbc"], ["hsbc.co.uk", "hsbc.com"]),
        new("NatWest", ["natwest"], ["natwest.com"]),
        new("Santander", ["santander"], ["santander.co.uk", "santander.com"]),
        new("Halifax", ["halifax"], ["halifax.co.uk"]),
        new("Nationwide", ["nationwide"], ["nationwide.co.uk"]),
        new("TSB", ["tsb"], ["tsb.co.uk"]),
        new("Monzo", ["monzo"], ["monzo.com"]),
        new("Vodafone", ["vodafone"], ["vodafone.co.uk", "vodafone.com"]),
        new("BT", ["bt"], ["bt.com"]),
        new("Sky", ["sky"], ["sky.com"]),
        new("Virgin Media", ["virgin media"], ["virginmedia.com"]),
        new("Tesco", ["tesco"], ["tesco.com"]),
        new("Northbank", ["northbank"], ["northbank.test"]),
    ];

    // Where anyone can open an account. A bank or a parcel firm does not write from these.
    // "freemail.example.net" is a made-up stand-in for the demo.
    private static readonly HashSet<string> FreeProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "outlook.co.uk", "hotmail.com", "hotmail.co.uk", "live.com", "live.co.uk", "msn.com",
        "yahoo.com", "yahoo.co.uk", "ymail.com", "icloud.com", "me.com", "aol.com", "aol.co.uk", "protonmail.com", "proton.me",
        "gmx.com", "mail.com", "btinternet.com", "talktalk.net", "freemail.example.net",
    };

    private static readonly HashSet<string> SecondLevel = new(StringComparer.OrdinalIgnoreCase)
        { "co", "com", "org", "net", "gov", "ac", "edu", "me", "ltd", "plc", "sch" };

    // Words around a name that do not change who it claims to be: "PayPal Security Team" is still PayPal.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "support", "team", "security", "service", "services", "customer", "customers", "care", "account", "accounts", "billing",
        "notification", "notifications", "alert", "alerts", "delivery", "online", "help", "helpdesk", "official", "the",
        "uk", "noreply", "no", "reply", "update", "updates", "info", "admin", "department", "payments", "payment", "refunds",
        "fraud", "verification", "verify", "id", "n", "co", "ltd", "of", "and", "from",
    };

    private static readonly Dictionary<string, string> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mum"] = "mother", ["mummy"] = "mother", ["mom"] = "mother", ["mommy"] = "mother", ["mam"] = "mother", ["mama"] = "mother", ["mother"] = "mother",
        ["dad"] = "father", ["daddy"] = "father", ["father"] = "father", ["papa"] = "father",
        ["nan"] = "grandmother", ["nana"] = "grandmother", ["nanna"] = "grandmother", ["nanny"] = "grandmother", ["gran"] = "grandmother",
        ["granny"] = "grandmother", ["grandma"] = "grandmother",
        ["grandad"] = "grandfather", ["granddad"] = "grandfather", ["grandpa"] = "grandfather", ["grandfather"] = "grandfather",
        ["son"] = "son", ["daughter"] = "daughter", ["grandson"] = "grandson", ["granddaughter"] = "granddaughter",
        ["sister"] = "sister", ["sis"] = "sister", ["brother"] = "brother", ["bro"] = "brother",
        ["aunt"] = "aunt", ["auntie"] = "aunt", ["aunty"] = "aunt", ["uncle"] = "uncle",
    };

    private static readonly HashSet<string> RoleFillers = new(StringComparer.OrdinalIgnoreCase) { "my", "your", "dear", "the", "love", "xx", "x" };

    // --- The check ---

    public IReadOnlyList<SenderSignal> Check(InboundMail mail, IReadOnlyList<Contact> contacts)
    {
        try { return Run(mail, contacts ?? []).OrderByDescending(s => s.Severity).ToList(); }
        catch (Exception) { return [Unverified()]; } // fail closed
    }

    private static SenderSignal Unverified() =>
        new("unverified-sender", SenderSeverity.High, "We could not read who sent this, so we cannot be sure it is from them.");

    private static List<SenderSignal> Run(InboundMail mail, IReadOnlyList<Contact> contacts)
    {
        if (mail is null) return [Unverified()];
        var name = mail.FromName ?? "";
        if (HasControl(name) || !TryParseAddress(mail.FromAddress, out var fromHost)) return [Unverified()];

        var signals = new List<SenderSignal>();
        string? replyHost = null;
        if (!string.IsNullOrWhiteSpace(mail.ReplyTo) && !TryParseAddress(mail.ReplyTo, out replyHost))
            signals.Add(Unverified());

        var fromAddress = Bare(mail.FromAddress);
        var knownAddress = contacts.Any(c => c.Address.Equals(fromAddress, StringComparison.OrdinalIgnoreCase));

        // The name shown carries another address or website than the real one.
        foreach (Match m in AddressOrDomain().Matches(name))
        {
            var shown = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant().TrimEnd('.');
            if (shown != fromHost && !fromHost.EndsWith("." + shown) && !shown.EndsWith("." + fromHost))
            {
                signals.Add(new("name-shows-other-address", SenderSeverity.High,
                    $"The name shown holds a different address ({D(shown)}) from the one it really came from."));
                break;
            }
        }

        // An address the user saved themselves is trusted: its name and domain are not questioned. Its Reply-To still is.
        if (!knownAddress)
        {
            NameChecks(name, fromHost, contacts, signals);
            if (Lookalike(fromHost) is { } look) signals.Add(new(look.Code, look.Severity, look.Reason));
        }

        // Reply-To: where an answer would really go.
        if (replyHost is not null)
        {
            if (Registrable(replyHost) != Registrable(fromHost))
                signals.Add(new("reply-to-differs", SenderSeverity.High,
                    $"If you reply, it goes to a different place ({D(replyHost)}) from the one it came from ({D(fromHost)})."));
            if (Lookalike(replyHost) is { } rl)
                signals.Add(new("reply-to-lookalike", rl.Severity, "The reply address is a problem too. " + rl.Reason));
        }

        return signals.DistinctBy(s => s.Code + s.Reason).ToList();
    }

    // Display name against address and against the saved contacts.
    private static void NameChecks(string name, string fromHost, IReadOnlyList<Contact> contacts, List<SenderSignal> signals)
    {
        var tokens = Tokens(name);
        if (tokens.Length == 0 || name.Contains('@')) return;

        var free = IsFree(fromHost);

        // 1. A known brand or office name on an address that is not theirs.
        if (BrandFromName(tokens) is { } brand && !IsBrandDomain(fromHost, brand))
        {
            signals.Add(new("brand-name-wrong-address", SenderSeverity.High, free
                ? $"The name says {brand.Name}, but the address is from a free email service ({D(fromHost)})."
                : $"The name says {brand.Name}, but the address ({D(fromHost)}) is not theirs."));
            return;
        }

        // 2. A saved contact's name on a different address.
        var role = RoleOf(tokens);
        var matches = contacts.Where(c => NameMatches(tokens, c.Name) || (role is not null && RoleOfText(c.Relationship) == role)).ToList();
        if (matches.Count > 0)
        {
            // A different address at the same company is normal (reception@ and reminders@). On a free service it is not,
            // because anyone can open a second account there.
            var sameCompany = matches.Any(c => TryParseAddress(c.Address, out var h) && Registrable(h) == Registrable(fromHost));
            if (sameCompany && !free) return;

            var c = matches[0];
            signals.Add(new("name-matches-contact", SenderSeverity.High, NameMatches(tokens, c.Name)
                ? $"The name is the same as {c.Name} in your contacts, but the address is not the one you saved."
                : $"The name says \"{Shown(name)}\". In your contacts that is {c.Name}, but the address is not the one you saved."));
            return;
        }

        // 3. Family names and ordinary names on free or unrelated addresses.
        if (role is not null)
        {
            signals.Add(free
                ? new("family-name-free-address", SenderSeverity.High,
                    $"The name says \"{Shown(name)}\", but the address is a free email address you have not saved ({D(fromHost)}).")
                : new("family-name-unknown-address", SenderSeverity.Medium,
                    $"The name says \"{Shown(name)}\", but the address is one you have not saved ({D(fromHost)})."));
        }
        else if (free && LooksLikePerson(name))
        {
            signals.Add(new("free-address-person", SenderSeverity.Low,
                $"This is a free email address ({D(fromHost)}). Anyone can make one with any name."));
        }
    }

    // --- Lookalike domains ---

    private sealed record Finding(string Code, SenderSeverity Severity, string Reason);

    private static Finding? Lookalike(string host)
    {
        if (Brands.Any(b => IsBrandDomain(host, b))) return null;

        // International letters (punycode "xn--" or plain Unicode) can copy Latin ones exactly.
        var isIdn = host.Split('.').Any(l => l.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)) || host.Any(c => c > 127);
        var unicodeHost = host;
        if (isIdn)
        {
            try { unicodeHost = new IdnMapping().GetUnicode(host); } catch (ArgumentException) { }
        }

        var (_, label, subs) = Split(isIdn ? InputGuard.Fold(unicodeHost).ToLowerInvariant() : host);
        var brandHit = Brands.FirstOrDefault(b => b.Label.Length >= 4 && (Skeleton(label) == Skeleton(b.Label) || Skeleton(label).Contains(Skeleton(b.Label))));
        if (isIdn)
            return new("idn-homograph", SenderSeverity.High, brandHit is not null
                ? $"The address ({D(host)}) is written with look-alike letters to copy {brandHit.Name}."
                : $"The address ({D(host)}) is written with look-alike letters, a trick used to copy real names.");

        foreach (var b in Brands.Where(b => b.Label.Length >= 4))
        {
            var bl = b.Label;
            var sl = Skeleton(label);
            var sb = Skeleton(bl);

            if (subs.Any(s => Skeleton(s) == sb))
                return new("brand-in-subdomain", SenderSeverity.High,
                    $"{D(host)} uses the name {b.Name} at the front to look real, but it belongs to someone else.");
            if (label == bl)
                return new("brand-other-domain", SenderSeverity.Medium,
                    $"{D(host)} uses the name {b.Name}, but it is not their usual address.");
            if (sl == sb)
                return new("lookalike-domain", SenderSeverity.High,
                    $"{D(host)} copies the address of {b.Name} but swaps some letters.");
            if (label.Split('-').Any(t => Skeleton(t) == sb) || (bl.Length >= 6 && sl.Contains(sb)))
                return new("lookalike-domain", SenderSeverity.High,
                    $"{D(host)} has the name {b.Name} in it to look real, but it is not their address.");
            var dist = EditDistance(label, bl);
            if (dist == 1 && bl.Length >= 5)
                return new("lookalike-domain", bl.Length >= 6 ? SenderSeverity.High : SenderSeverity.Medium,
                    $"{D(host)} is almost the same as the {b.Name} address, with one letter changed.");
            if (dist == 2 && bl.Length >= 8)
                return new("lookalike-domain", SenderSeverity.Medium,
                    $"{D(host)} is very close to the {b.Name} address, with two letters changed.");
        }
        return null;
    }

    // --- Helpers ---

    private static bool IsFree(string host) => FreeProviders.Contains(host) || FreeProviders.Contains(Registrable(host));

    private static bool IsBrandDomain(string host, Brand b) => b.Domains.Any(d => host == d || host.EndsWith("." + d));

    private static Brand? BrandFromName(string[] tokens)
    {
        var core = tokens.Where(t => !Generic.Contains(t)).ToArray();
        if (core.Length is 0 or > 3) return null;
        var joined = Skeleton(string.Concat(core));
        return Brands.FirstOrDefault(b => b.Aliases.Any(a => Skeleton(a.Replace(" ", "")) == joined));
    }

    private static string? RoleOf(string[] tokens)
    {
        var core = tokens.Where(t => !RoleFillers.Contains(t)).ToArray();
        return core.Length == 1 && Roles.TryGetValue(core[0], out var r) ? r : null;
    }

    private static string? RoleOfText(string? text)
    {
        var first = Tokens(text ?? "").FirstOrDefault();
        return first is not null && Roles.TryGetValue(first, out var r) ? r : null;
    }

    // "Priya Sandhu" matches "Priya Sandhu" and "Sandhu Priya", and "Priya" on its own matches the contact's first name.
    private static bool NameMatches(string[] shown, string contactName)
    {
        var theirs = Tokens(contactName);
        if (shown.Length == 0 || theirs.Length == 0) return false;
        if (!shown.All(theirs.Contains)) return false;
        return shown.Length >= 2 || shown[0] == theirs[0];
    }

    private static bool LooksLikePerson(string name) => PersonName().IsMatch(name.Trim());

    [GeneratedRegex(@"^\p{Lu}[\p{L}'’-]+(?: \p{Lu}[\p{L}'’-]+){1,2}$")]
    private static partial Regex PersonName();

    [GeneratedRegex(@"[^\s@<>""']+@([^\s@<>""']+)|\b((?:[a-z0-9-]+\.)+[a-z]{2,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddressOrDomain();

    private static string[] Tokens(string text) =>
        Regex.Split(InputGuard.Fold(text).ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(t => t.Length > 0).ToArray();

    private static string Shown(string name) => InputGuard.Fold(name).Trim();

    private static string D(string host) => host.Replace(".", "[.]");

    private static bool HasControl(string s) => s.Any(char.IsControl);

    private static string Bare(string? raw)
    {
        var s = (raw ?? "").Trim();
        var lt = s.LastIndexOf('<');
        return lt >= 0 && s.EndsWith('>') ? s[(lt + 1)..^1].Trim() : s;
    }

    /// <summary>One address ("a@b.c" or "Name &lt;a@b.c&gt;") with a readable host. Anything odd is refused, never repaired.</summary>
    private static bool TryParseAddress(string? raw, out string host)
    {
        host = "";
        var s = Bare(raw);
        if (s.Length is 0 or > 254) return false;
        if (s.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or ';' or '<' or '>' or '"' or '(' or ')' or '\\')) return false;
        var at = s.IndexOf('@');
        if (at < 1 || at > 64 || at != s.LastIndexOf('@')) return false;

        var h = s[(at + 1)..].TrimEnd('.').ToLowerInvariant();
        if (h.Length is < 3 or > 253 || !h.Contains('.')) return false;
        foreach (var label in h.Split('.'))
        {
            if (label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-')) return false;
            if (!label.All(c => char.IsLetterOrDigit(c) || c == '-')) return false;
        }
        host = h;
        return true;
    }

    private static (string Registrable, string Label, string[] Subs) Split(string host)
    {
        var labels = host.Split('.');
        var take = labels.Length >= 3 && labels[^1].Length == 2 && SecondLevel.Contains(labels[^2]) ? 3 : 2;
        take = Math.Min(take, labels.Length);
        return (string.Join('.', labels[^take..]), labels[^take], labels[..^take]);
    }

    private static string Registrable(string host) => Split(host).Registrable;

    // Letters that are easy to mistake for each other, folded together so "paypa1" and "paypal" compare equal.
    private static string Skeleton(string s)
    {
        var t = InputGuard.Fold(s).ToLowerInvariant().Replace("rn", "m").Replace("vv", "w");
        return string.Create(t.Length, t, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = src[i] switch { '0' => 'o', '1' => 'l', 'i' => 'l', '3' => 'e', '5' => 's', '$' => 's', _ => src[i] };
        });
    }

    // Optimal string alignment distance: one letter added, removed, changed or two neighbours swapped each count as 1.
    private static int EditDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
