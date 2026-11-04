using Backstop.Models;

namespace Backstop.Services.Api;

// Each side's own profile: picture and online / hidden. The other side sees Hidden as offline.
public sealed partial class MockBackstopApi
{
    private const int MaxProfilePhotoBytes = 3 * 1024 * 1024;

    private readonly Dictionary<ProfileRole, Presence> _presence = [];
    private readonly Dictionary<ProfileRole, string> _profilePhotoVersion = [];

    public Profile GetProfile(ProfileRole role)
    {
        lock (_gate)
            return new Profile(role, role == ProfileRole.User ? Household.UserName : Household.TrustedPersonName,
                _presence.GetValueOrDefault(role, Presence.Online), _profilePhotoVersion.GetValueOrDefault(role));
    }

    public void SetPresence(ProfileRole role, Presence presence)
    {
        lock (_gate) _presence[role] = presence;
        RaiseChanged();
    }

    public bool SetProfilePhoto(ProfileRole role, byte[] bytes)
    {
        // Trust the bytes, never the file name or the browser's claim. SVG is refused (it can carry script).
        if (bytes.Length == 0 || bytes.Length > MaxProfilePhotoBytes) return false;
        var type = ImageSniff.ImageType(bytes);
        if (type is null) return false;
        lock (_gate)
        {
            _photos[Profile.PhotoKey(role)] = new ContactPhoto(bytes, type);
            _profilePhotoVersion[role] = Guid.NewGuid().ToString("N")[..8];
        }
        RaiseChanged();
        return true;
    }

    public void RemoveProfilePhoto(ProfileRole role)
    {
        lock (_gate)
        {
            _photos.Remove(Profile.PhotoKey(role));
            _profilePhotoVersion.Remove(role);
        }
        RaiseChanged();
    }

    // Caller holds the lock.
    private void ResetProfiles()
    {
        _presence.Clear();
        _profilePhotoVersion.Clear();
    }
}
