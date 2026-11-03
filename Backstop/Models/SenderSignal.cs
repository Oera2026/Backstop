namespace Backstop.Models;

/// <summary>How much a sender warning matters. High always means a person looks first; the others are shown as evidence.</summary>
public enum SenderSeverity { Low, Medium, High }

/// <summary>
/// One thing about WHO sent a message that looks wrong. <see cref="Code"/> is stable (for tests and code),
/// <see cref="Reason"/> is one short plain sentence for the helper. Worked out from the headers only, never by AI.
/// </summary>
public sealed record SenderSignal(string Code, SenderSeverity Severity, string Reason)
{
    /// <summary>True when any signal is High. A High signal means the message can never be cleared without a person.</summary>
    public static bool NeedsHold(IEnumerable<SenderSignal> signals) => signals.Any(s => s.Severity == SenderSeverity.High);
}
