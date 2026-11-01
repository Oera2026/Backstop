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

/// <summary>How much a helper can do. Custom means the user ticked each capability themselves.</summary>
public enum HelperTier { Minimal, Standard, Custom, Administrator }

/// <summary>The user's choice, kept per household. Created in the setup flow and editable in Settings.</summary>
public sealed record HelperPermissions(HelperTier Tier, HelperCapability Custom = HelperCapability.None)
{
    public static readonly HelperPermissions Default = new(HelperTier.Minimal);

    /// <summary>What each named tier allows. The floor is part of every one.</summary>
    public static HelperCapability For(HelperTier tier, HelperCapability custom = HelperCapability.None) => HelperCapability.Floor | tier switch
    {
        HelperTier.Minimal => HelperCapability.None,
        HelperTier.Standard => HelperCapability.SeeCheckRequests | HelperCapability.SeeSharedMemories | HelperCapability.SeeActivitySummary,
        HelperTier.Custom => custom,
        HelperTier.Administrator => HelperCapability.SeeCheckRequests | HelperCapability.SeeSharedMemories | HelperCapability.SeeActivitySummary
                                    | HelperCapability.SeeClearedMail | HelperCapability.SeeFullActivity | HelperCapability.SendAsUser
                                    | HelperCapability.SeeChats,
        _ => HelperCapability.None, // unknown tier: floor only (fail closed)
    };

    public HelperCapability Granted => For(Tier, Custom);

    public bool Allows(HelperCapability capability) => (Granted & capability) == capability;
}
