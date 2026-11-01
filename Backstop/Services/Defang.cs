using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>Makes links harmless to display. Nothing here is ever rendered as a clickable link.</summary>
public static partial class Defang
{
    [GeneratedRegex(@"\b(https?)://([^\s/]+)(\S*)", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeUrl();

    /// <summary>hxxp://example[.]com style, for the helper. Covers https:// links, www. links and bare addresses.</summary>
    public static string ForHelper(string text) =>
        LinkFinder.Pattern.Replace(text, m => DefangOne(m.Value));

    /// <summary>Plain wording, for the user. Links are removed rather than shown in a confusing form.</summary>
    public static string ForUser(string text) =>
        LinkFinder.Pattern.Replace(text, "[link removed]");

    public static string Url(string url) => ForHelper(url);

    private static string DefangOne(string link)
    {
        if (SchemeUrl().Match(link) is { Success: true, Index: 0 } s)
            return $"hxxp{s.Groups[1].Value[4..]}://{s.Groups[2].Value.Replace(".", "[.]")}{s.Groups[3].Value}";
        var slash = link.IndexOf('/');
        var host = slash < 0 ? link : link[..slash];
        return host.Replace(".", "[.]") + (slash < 0 ? "" : link[slash..]);
    }
}
