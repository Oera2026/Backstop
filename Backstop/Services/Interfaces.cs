using Backstop.Models;

namespace Backstop.Services;

/// <summary>What the USER side can do. A real backend replaces the Mock implementation.</summary>
public interface IMailService
{
    /// <summary>Raised after any change to any message. Handlers may run on any thread.</summary>
    event Action? Changed;

    /// <summary>Cleared mail only. Held, blocked and unknown states are never returned.</summary>
    IReadOnlyList<MailMessage> GetInbox();

    /// <summary>How many messages the helper is checking. The user is told only this number.</summary>
    int HeldCount { get; }

    /// <summary>Returns the message only if it is Cleared. Fail closed: otherwise null.</summary>
    MailMessage? GetClearedMessage(string id);

    OutgoingCheck CheckOutgoing(string to, string subject, string body);

    /// <summary>MOCK: pretends to send. Nothing leaves the app.</summary>
    void Send(string to, string subject, string body);

    // --- Demo support ---
    IReadOnlyList<Scenario> Scenarios { get; }

    /// <summary>
    /// Runs the mock pipeline for a scenario. <paramref name="onTier"/> is called when a tier starts
    /// (status Running) and when it finishes. Any error ends in Failed -> Held.
    /// </summary>
    Task<MailMessage?> ReceiveAsync(string scenarioId, bool simulateFailure, Action<int, TierResult>? onTier = null);

    void Reset();
}

/// <summary>What the HELPER (guardian) side can do.</summary>
public interface IGuardianService
{
    event Action? Changed;

    IReadOnlyList<MailMessage> GetQueue();

    /// <summary>Messages already decided (approved, blocked by a helper, or auto-blocked).</summary>
    IReadOnlyList<MailMessage> GetDecided();

    MailMessage? Get(string id);

    void Approve(string id);
    void Block(string id);
    void CallFirst(string id);
}
