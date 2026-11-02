namespace Backstop.Services.Api;

/// <summary>
/// A key typed in at the local start-up prompt. Held in memory only: never saved, logged or sent to a browser.
/// Used only when no key is set in the environment. Lost when the app stops.
/// </summary>
public static class MetaKeyStore
{
    private static volatile string? _key;

    public static string? Key => _key;

    public static void Set(string key) => _key = key.Trim();
}
