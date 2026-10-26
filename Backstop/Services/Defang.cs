using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>Makes links harmless to display. Nothing here is ever rendered as a clickable link.</summary>
public static partial class Defang
{
    [GeneratedRegex(@"\b(https?)://([^\s/]+)(\S*)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>hxxp://example[.]com style, for the helper.</summary>
    public static string ForHelper(string text) =>
        UrlPattern().Replace(text, m => $"hxxp{m.Groups[1].Value[4..]}://{m.Groups[2].Value.Replace(".", "[.]")}{m.Groups[3].Value}");

    /// <summary>Plain wording, for the user. Links are removed rather than shown in a confusing form.</summary>
    public static string ForUser(string text) =>
        UrlPattern().Replace(text, "[link removed for safety]");

    public static string Url(string url) => ForHelper(url);
}
