namespace Backstop.Models;

/// <summary>Which side of the app a profile belongs to.</summary>
public enum ProfileRole { User, Helper }

/// <summary>Online shows others you are here. Hidden makes you look offline to everyone else; you still see yourself as Hidden.</summary>
public enum Presence { Online, Hidden }

/// <summary>A person's own profile: name, picture and whether they show as online. MOCK: kept in memory.</summary>
public sealed record Profile(ProfileRole Role, string Name, Presence Presence, string? PhotoVersion)
{
    /// <summary>Where the picture is served from, or null when there is none.</summary>
    public string? PhotoUrl => PhotoVersion is null ? null : $"/mode-photo/{PhotoKey(Role)}?v={PhotoVersion}";

    public static string PhotoKey(ProfileRole role) => $"profile-{role.ToString().ToLowerInvariant()}";
}
