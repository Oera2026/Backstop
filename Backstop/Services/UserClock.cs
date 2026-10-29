namespace Backstop.Services;

/// <summary>
/// Shows times in the reader's own time zone, not the server's. The browser sends its zone in the
/// <c>backstop.tz</c> cookie (see js/settings.js). Without it, the server's zone is used.
/// </summary>
public sealed class UserClock(TimeZoneInfo zone)
{
    public static UserClock Server { get; } = new(TimeZoneInfo.Local);

    public static UserClock For(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && timeZoneId.Length <= 64
            && TimeZoneInfo.TryFindSystemTimeZoneById(Uri.UnescapeDataString(timeZoneId), out var zone)
            ? new UserClock(zone)
            : Server;

    public DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone);

    public DateOnly Today => DateOnly.FromDateTime(Local(DateTimeOffset.UtcNow).DateTime);

    /// <summary>"Today, 6:52 PM", "Yesterday, 6:52 PM" or "Mon 26 Oct, 6:52 PM".</summary>
    public string When(DateTimeOffset at)
    {
        var local = Local(at);
        var day = DateOnly.FromDateTime(local.DateTime);
        var time = local.ToString("h:mm tt");
        if (day == Today) return $"Today, {time}";
        if (day == Today.AddDays(-1)) return $"Yesterday, {time}";
        return $"{local:ddd d MMM}, {time}";
    }

    /// <summary>"Tuesday 27 October, 6:52 PM".</summary>
    public string Long(DateTimeOffset at) => Local(at).ToString("dddd d MMMM, h:mm tt");
}
