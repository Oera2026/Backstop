using System.Security.Cryptography;
using System.Text;
using Backstop.Services.Api;

namespace Backstop.Services;

public sealed record DemoResetResult(bool Done, string? Problem);

/// <summary>Controls on the /demo page that change shared state. Each one needs the secret.</summary>
public interface IDemoControl
{
    /// <summary>Wipes the demo household and puts the 7 scenarios back. Needs the secret.</summary>
    DemoResetResult Reset(string? secret);

    /// <summary>Empties Remember for everyone. Needs the same secret, because the demo household is shared by every visitor.</summary>
    DemoResetResult ClearMemories(string? secret);
}

/// <summary>
/// Reset is protected by DEMO_RESET_SECRET. With no secret set, reset is off (fail closed). Wrong guesses are
/// slowed down: five wrong tries in a row switch it off for a minute for everyone. The check is made here, not in
/// the page, so a page cannot skip it.
/// </summary>
public sealed class DemoControl(IMailService mail, IBackstopApi api, MockApiOptions options, IConfiguration config, ILogger<DemoControl> logger) : IDemoControl
{
    private const int MaxWrong = 5;
    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(1);
    private readonly object _gate = new();
    private int _wrong;
    private DateTime _lockedUntil;

    public DemoResetResult Reset(string? secret)
    {
        if (Refuse(secret) is { } no) return no;
        mail.Reset();
        api.ResetDemo();
        options.ResetAll();
        return new(true, null);
    }

    public DemoResetResult ClearMemories(string? secret)
    {
        if (Refuse(secret) is { } no) return no;
        api.ClearMemories();
        return new(true, null);
    }

    // Null when the secret is right. Otherwise why not, with wrong guesses counted towards the lock.
    private DemoResetResult? Refuse(string? secret)
    {
        var expected = config["DEMO_RESET_SECRET"];
        if (string.IsNullOrEmpty(expected))
        {
            logger.LogWarning("Demo control refused: DEMO_RESET_SECRET is not set.");
            return new(false, "Reset is switched off.");
        }

        lock (_gate)
        {
            if (DateTime.UtcNow < _lockedUntil) return new(false, "Too many tries. Please wait a minute.");
            if (!Matches(secret, expected))
            {
                if (++_wrong >= MaxWrong)
                {
                    _wrong = 0;
                    _lockedUntil = DateTime.UtcNow + Lock;
                    logger.LogWarning("Demo reset locked for a minute after repeated wrong codes.");
                }
                return new(false, "That is not the right code.");
            }
            _wrong = 0;
        }
        return null;
    }

    /// <summary>Same time whatever the guess, and whatever its length.</summary>
    private static bool Matches(string? given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
