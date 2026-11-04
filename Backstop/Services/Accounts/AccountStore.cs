using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Backstop.Services.Accounts;

/// <summary>The three kinds of signed-in visitor. Demo may see both sides.</summary>
public static class Roles
{
    public const string User = "User";
    public const string Helper = "Helper";
    public const string Demo = "Demo";

    /// <summary>Roles allowed on the user side.</summary>
    public const string UserSide = User + "," + Demo;
    /// <summary>Roles allowed on the helper side.</summary>
    public const string HelperSide = Helper + "," + Demo;
}

public sealed record Account(string Email, string DisplayName, string Role, byte[] Salt, byte[] Hash);

/// <summary>
/// Made-up accounts for this release (example.com addresses only). There is no sign-up.
/// Passwords come from the environment (ACCOUNT_USER_PASSWORD, ACCOUNT_HELPER_PASSWORD) and are never stored in the
/// repo or kept in plain text. With no password set, that account cannot sign in (fail closed); the demo still works.
/// </summary>
public sealed class AccountStore
{
    private const int Iterations = 210_000;
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _failures = new(StringComparer.OrdinalIgnoreCase);

    public AccountStore(IConfiguration config)
    {
        Seed("sam@example.com", "Margaret", Roles.User, config["ACCOUNT_USER_PASSWORD"]);
        Seed("alex@example.com", "Priya", Roles.Helper, config["ACCOUNT_HELPER_PASSWORD"]);
    }

    private void Seed(string email, string name, string role, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return;
        var salt = RandomNumberGenerator.GetBytes(16);
        _accounts[email] = new Account(email, name, role, salt, Hash(password, salt));
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private const int MaxTracked = 5000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns the account when the email and password match; otherwise null. Slows down repeated misses.
    /// The lock is per <paramref name="visitor"/> and email, so a stranger's wrong guesses cannot lock the real person out.
    /// </summary>
    public Account? Verify(string? email, string? password, string? visitor = null)
    {
        email = (email ?? "").Trim();
        if (email.Length is 0 or > 200 || string.IsNullOrEmpty(password) || password.Length > 200) return null;
        var key = $"{visitor ?? "unknown"}|{email}";

        // The try is counted BEFORE the slow check, in one step, so a burst of parallel guesses cannot all slip in ahead of the lock.
        if (_failures.Count >= MaxTracked) Sweep();
        var now = DateTime.UtcNow;
        var tries = _failures.AddOrUpdate(key, (1, now), (_, f) => now - f.Since > Window ? (1, now) : (f.Count + 1, f.Since));
        if (tries.Count > MaxTries) return null;

        // Same work whether or not the account exists, so timing does not reveal which emails are real.
        _accounts.TryGetValue(email, out var account);
        var hash = Hash(password, account?.Salt ?? new byte[16]);
        var ok = account is not null && CryptographicOperations.FixedTimeEquals(hash, account.Hash);
        if (ok) _failures.TryRemove(key, out _);
        return ok ? account : null;
    }

    private const int MaxTries = 5;

    // Forgets misses older than the window, so typed-in emails cannot fill memory. Still full: start again.
    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var (k, f) in _failures)
            if (now - f.Since > Window) _failures.TryRemove(k, out _);
        if (_failures.Count >= MaxTracked) _failures.Clear();
    }

    public static ClaimsPrincipal Principal(string name, string role, string authType = "backstop")
    {
        var id = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, role)],
            authType);
        return new ClaimsPrincipal(id);
    }
}
