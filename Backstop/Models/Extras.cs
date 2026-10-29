namespace Backstop.Models;

/// <summary>Someone the user knows. Synthetic data only (example.com / .net / .org).</summary>
public sealed record Contact(string Id, string Name, string Address, string Relationship, string? PhotoVersion = null)
{
    /// <summary>Where the profile picture is served from, or null when the contact has none.</summary>
    public string? PhotoUrl => PhotoVersion is null ? null : $"/contact-photo/{Id}?v={PhotoVersion}";
}

/// <summary>A contact's picture. MOCK: kept in memory only.</summary>
public sealed record ContactPhoto(byte[] Bytes, string ContentType);

/// <summary>A message the user has sent. MOCK: kept in memory, nothing leaves the app.</summary>
public sealed class SentMessage
{
    public required string Id { get; init; }
    public string ToName { get; init; } = "";
    public required string ToAddress { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.Now;
}

public sealed class CalendarEvent
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required DateOnly Date { get; init; }
    /// <summary>Null means "all day".</summary>
    public TimeOnly? Time { get; init; }
    public string Place { get; init; } = "";
}

/// <summary>Tells the helper something happened that they should know about (not something they must decide).</summary>
public sealed class HelperNotice
{
    public required string Id { get; init; }
    public required string Address { get; init; }
    public required string Subject { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public bool Seen { get; set; }
}

public sealed record SearchResults(
    IReadOnlyList<MailMessage> Messages,
    IReadOnlyList<SentMessage> Sent,
    IReadOnlyList<Contact> Contacts)
{
    public int Total => Messages.Count + Sent.Count + Contacts.Count;
}

/// <summary><see cref="Refused"/> is set (and <see cref="Message"/> is null) when nothing was sent.</summary>
public sealed record SendResult(SentMessage? Message, bool NewRecipient, string? Refused);
