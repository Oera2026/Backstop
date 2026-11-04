namespace Backstop.Services;

public enum TourSide { User, Helper }

/// <summary>
/// One slide. Selector is the thing to light up. Tap means the thing is a link to the next page, so
/// "Show me" taps it for the visitor. A Path with ":id" stands for any one message page.
/// </summary>
public sealed record TourStep(string Path, string Title, string Text, string Selector, bool Tap = false)
{
    /// <summary>The place in the navigation bar this slide belongs to.</summary>
    public string Section { get; init; } = "";

    public bool Concrete => !Path.Contains(':');
}

/// <summary>Where a demo visitor is in the guided walk-around. One per browser tab (circuit). One side at a time, in page order.</summary>
public sealed class DemoTour
{
    private static readonly string[] HelperPages = ["messages", "history", "settings", "write", "check", "story"];

    private static TourStep S(string section, string path, string title, string text, string selector, bool tap = false) =>
        new(path, title, text, selector, tap) { Section = section };

    // One place at a time, in the order of the navigation bar: its button, then the one thing that matters there.
    public static readonly IReadOnlyList<TourStep> UserSteps =
    [
        S("Home", "home", "Home", "Your start page. Big buttons for the things you do most.", ".appnav__item[href='home'], .site-header .brand"),
        S("Inbox", "/", "Inbox", "Your mail. Tap a message to read it.", ".home__list .row, .emptystate"),
        S("Inbox", "/", "Write", "Start a new message here.", ".home__side .btn-primary"),
        S("Ask Me", "ask-me", "Ask Me", "Type a question in plain words. You get a plain answer.", ".composer textarea"),
        S("Ask Me", "check", "Check a message", "Not sure about a message? Paste it here.", "#check-text"),
        S("Messages", "messages", "Messages", "Chats with the people you know.", ".appnav__item[href='messages']"),
        S("Remember", "remember", "Remember", "Ask how you did something before.", ".askbox .askrow"),
        S("Settings", "settings", "Settings", "Make the words bigger. Change the colours.", "#set-read"),
    ];

    public static readonly IReadOnlyList<TourStep> HelperSteps =
    [
        S("Waiting", "/helper", "Waiting", "Mail the checker was unsure about waits here. The most urgent is first.", ".hero, .rows a.row"),
        S("Waiting", "/helper", "Review it", "Tap Review it to see why it was held.", ".hero__go, .rows a.row", true),
        S("Waiting", "helper/:id", "Your decision", "Safe lets it through. Not safe blocks it.", ".actionbar"),
        S("Messages", "helper/messages", "Messages", "Chats with the user and others.", ".msgs__list"),
        S("History", "helper/history", "History", "Every decision is kept here.", ".appnav__item[href='/helper/history']"),
        S("Settings", "helper/settings", "Settings", "What you can and cannot see.", "#ha-title"),
    ];

    private readonly Dictionary<int, string> _seen = [];

    public TourSide Side { get; private set; } = TourSide.User;
    public bool Active { get; private set; }
    public int Index { get; private set; }

    /// <summary>The places this side visits, in order. A place with no slides is not listed.</summary>
    public IEnumerable<string> Sections => Steps.Select(s => s.Section).Distinct();
    public IReadOnlyList<TourStep> Steps => Side == TourSide.User ? UserSteps : HelperSteps;
    public TourStep Current => Steps[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Steps.Count - 1;

    /// <summary>True once the guide has been started or put back for this circuit.</summary>
    public bool Begun { get; private set; }

    public event Action? Changed;

    public static TourSide SideOf(string relativePath) =>
        Norm(relativePath).Split('/')[0] == "helper" ? TourSide.Helper : TourSide.User;

    private static string Norm(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();

    public static bool Matches(TourStep step, string relativePath)
    {
        var p = Norm(relativePath);
        if (step.Concrete) return Norm(step.Path) == p;
        var parts = p.Split('/');
        return parts.Length == 2 && parts[0] == "helper" && !HelperPages.Contains(parts[1]);
    }

    public void Next() { if (!IsLast) { Index++; Changed?.Invoke(); } }
    public void Back() { if (!IsFirst) { Index--; Changed?.Invoke(); } }
    public void Close() { Active = false; Changed?.Invoke(); }

    /// <summary>Begins the walk-around for one side, from its first slide.</summary>
    public void Start(TourSide side)
    {
        Begun = true;
        Side = side;
        Index = 0;
        Active = true;
        _seen.Clear();
        Changed?.Invoke();
    }

    /// <summary>Puts the walk-around back where this browser tab left it.</summary>
    public void Restore(TourSide side, int index, bool active)
    {
        Begun = true;
        Side = side;
        Index = Math.Clamp(index, 0, Steps.Count - 1);
        Active = active;
        _seen.Clear();
        Changed?.Invoke();
    }

    /// <summary>The visitor moved by hand: if that page has slides, the guide follows them.</summary>
    public void Follow(string relativePath)
    {
        if (Matches(Current, relativePath))
        {
            if (!Current.Concrete) _seen[Index] = Norm(relativePath);
            return;
        }
        var i = Steps.ToList().FindIndex(s => Matches(s, relativePath));
        if (i < 0) return;
        Index = i;
        if (!Steps[i].Concrete) _seen[i] = Norm(relativePath);
        Changed?.Invoke();
    }

    /// <summary>The page to open for the current slide, or null when it is a message page we have not seen yet.</summary>
    public string? PathForCurrent() => Current.Concrete ? Current.Path : _seen.GetValueOrDefault(Index) is { } p ? "/" + p : null;

    public string Save() => $"{Side}|{Index}|{(Active ? 1 : 0)}";

    public static bool TryParse(string? saved, out TourSide side, out int index, out bool active)
    {
        side = TourSide.User; index = 0; active = true;
        var parts = (saved ?? "").Split('|');
        if (parts.Length != 3 || !Enum.TryParse(parts[0], out side) || !int.TryParse(parts[1], out index)) return false;
        active = parts[2] == "1";
        return true;
    }
}
