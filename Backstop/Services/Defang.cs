using System.Text;
using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>
/// Makes links harmless to display. Nothing here is ever rendered as a clickable link.
/// Links are looked for in a folded copy, so hidden characters and look-alike dots cannot keep one in the text. Every other word stays as written.
/// </summary>
public static class Defang
{
    /// <summary>hxxp://example[.]com style, for the helper. Covers web links, other schemes, www. links, bare addresses and bare numbers.</summary>
    public static string ForHelper(string text) => Rewrite(text, DefangOne);

    /// <summary>Plain wording, for the user. Links are removed rather than shown in a confusing form.</summary>
    public static string ForUser(string text) => Rewrite(text, _ => "[link removed]");

    public static string Url(string url) => ForHelper(url);

    private static string Rewrite(string text, Func<string, string> replace)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try
        {
            var folded = InputGuard.FoldWithMap(text, out var map);
            StringBuilder? sb = null;
            var at = 0;
            foreach (Match m in LinkFinder.Pattern.Matches(folded))
            {
                var start = map[m.Index];
                if (start < at) continue;
                sb ??= new StringBuilder(text.Length);
                sb.Append(text, at, start - at).Append(replace(m.Value));
                at = map[m.Index + m.Length];
            }
            return sb is null ? text : sb.Append(text, at, text.Length - at).ToString();
        }
        catch (RegexMatchTimeoutException)
        {
            // Could not finish looking, so nothing in it can work as a link.
            return text.Replace(".", "[.]").Replace("://", "[:]//");
        }
    }

    private static string DefangOne(string link)
    {
        var gap = link.IndexOf("://", StringComparison.Ordinal);
        var scheme = "";
        var rest = link;
        if (gap > 0)
        {
            var name = link[..gap];
            scheme = name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? $"hxxp{name[4..]}://" : $"{name}[:]//";
            rest = link[(gap + 3)..];
        }
        var slash = rest.IndexOf('/');
        var host = slash < 0 ? rest : rest[..slash];
        return scheme + host.Replace(".", "[.]") + (slash < 0 ? "" : rest[slash..]);
    }
}
