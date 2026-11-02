namespace Backstop.Models;

/// <summary>One thing a helper may be allowed to do or see. The user picks these when the account is set up.</summary>
[Flags]
public enum HelperCapability
{
    None = 0,

    /// <summary>Always on, in every tier: a helper is told when a risky email arrives.</summary>
    RiskAlerts = 1 << 0,

    /// <summary>Always on, in every tier: a helper approves or blocks held mail. Without it nothing held could ever be cleared.</summary>
    ReviewHeldMail = 1 << 1,

    SeeCheckRequests = 1 << 2,
    SeeSharedMemories = 1 << 3,
    SeeActivitySummary = 1 << 4,
    SeeClearedMail = 1 << 5,
    SeeFullActivity = 1 << 6,
    SendAsUser = 1 << 7,

    /// <summary>Read the user's normal chats. Held chats are always visible, because a helper must be able to decide them.</summary>
    SeeChats = 1 << 8,

    /// <summary>The two things no tier can switch off. Held mail fails closed, so a helper must always be able to hear about it and decide it.</summary>
    Floor = RiskAlerts | ReviewHeldMail,
}

/// <summary>
/// How much a helper can do. This release has the Standard tier only; Minimal is the one stricter setting.
/// Administrator and custom access are not built.
/// </summary>
public enum HelperTier { Minimal, Standard }

/// <summary>The helper's access, kept per household. It is fixed at Standard in this release.</summary>
public sealed record HelperPermissions(HelperTier Tier)
{
    /// <summary>The default, and the most any helper can have in this release.</summary>
    public static readonly HelperPermissions Default = new(HelperTier.Standard);

    /// <summary>
    /// Anything above Standard (or a tier value that does not exist) becomes Standard. Minimal stays Minimal, because
    /// stricter is always allowed. Services call this, so a page or a caller cannot ask for more.
    /// </summary>
    public static HelperPermissions Clamp(HelperPermissions? access) => access?.Tier == HelperTier.Minimal ? access : Default;

    /// <summary>What each tier allows. The floor is part of every one. An unknown tier gets the floor only (fail closed).</summary>
    public static HelperCapability For(HelperTier tier) => HelperCapability.Floor | tier switch
    {
        HelperTier.Minimal => HelperCapability.None,
        HelperTier.Standard => HelperCapability.SeeCheckRequests | HelperCapability.SeeSharedMemories | HelperCapability.SeeActivitySummary,
        _ => HelperCapability.None,
    };

    public HelperCapability Granted => For(Clamp(this).Tier);

    public bool Allows(HelperCapability capability) => (Granted & capability) == capability;
}
