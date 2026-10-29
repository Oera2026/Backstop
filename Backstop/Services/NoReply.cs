namespace Backstop.Services;

/// <summary>Spots addresses nobody reads, so the user is never allowed to write to them.</summary>
public static class NoReply
{
    private static readonly string[] Markers = ["noreply", "donotreply", "mailerdaemon", "postmaster", "bounce"];

    public static bool IsNoReply(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var at = address.IndexOf('@');
        var local = (at < 0 ? address : address[..at]).ToLowerInvariant();
        // "no-reply", "no_reply", "do.not.reply" and "noreply" all count.
        var squashed = local.Replace("-", "").Replace("_", "").Replace(".", "").Replace("+", "");
        return Markers.Any(squashed.Contains);
    }
}
