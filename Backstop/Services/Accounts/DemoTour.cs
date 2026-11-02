namespace Backstop.Services.Accounts;

public enum TourSide { User, Helper }

/// <summary>
/// One slide. Selector is the thing to light up. Tap means the thing is a link to the next page, so
/// "Show me" taps it for the visitor. A Path with ":id" stands for any one message page.
/// </summary>
public sealed record TourStep(string Path, string Title, string Text, string Selector, bool Tap = false)
{
    public bool Concrete => !Path.Contains(':');
}

/// <summary>Where a demo visitor is in the guided walk-around. One per browser tab (circuit). One side at a time, in page order.</summary>
public sealed class DemoTour
{
    private static readonly string[] HelperPages = ["messages", "history", "settings", "write", "check", "story"];

    public static readonly IReadOnlyList<TourStep> UserSteps =
    [
        new("/", "Your inbox", "Safe mail shows here. Mail still being checked stays hidden.", "main h1"),
        new("/", "Write", "Start a new message here.", ".home__side .btn-primary"),
        new("/", "Shortcuts", "Contacts, sent mail, calendar and search.", ".tiles"),
        new("/", "A message", "Tap a message to read it.", ".home__list .row, .emptystate"),
        new("/", "Next: Ask Me", "Tap Ask Me.", ".appnav__item[href='ask-me']", true),

        new("ask-me", "Ask Me", "Type a question here.", ".composer textarea"),
        new("ask-me", "Send", "Press Send. Ask Me answers in plain words.", ".composer button[type='submit']"),
        new("ask-me", "Next: Messages", "Tap Messages.", ".appnav__item[href='messages']", true),

        new("messages", "Messages", "Chats with the people you know. Tap one to open it.", ".rows .row"),
        new("messages", "Next: Remember", "Tap Remember.", ".appnav__item[href='remember']", true),

        new("remember", "Remember", "Ask how you did something before.", ".askbox .askrow"),
        new("remember", "Ideas", "Tap an idea if you are not sure what to ask.", ".chips"),
        new("remember", "Next: Settings", "Tap Settings.", ".site-nav a[href='/settings']", true),

        new("settings", "Reading", "Make the words bigger. Slow the voice down.", "#set-read"),
        new("settings", "Colours", "Light or dark. Stronger contrast. Less movement.", "#set-look"),
        new("settings", "All done", "Press Done to go back.", ".settings-foot .btn-primary"),
    ];

    public static readonly IReadOnlyList<TourStep> HelperSteps =
    [
        new("/helper", "Waiting for you", "Mail the checker was unsure about waits here.", "main h1"),
        new("/helper", "A message", "Tap one to see why it was held.", ".rows a.row", true),

        new("helper/:id", "Who sent it", "See who it is from and what it says.", ".evidence h1"),
        new("helper/:id", "The checker's view", "A short reason, in plain words.", ".evidence .lead"),
        new("helper/:id", "Warning signs", "Each thing that looked wrong is listed.", "#ev-flags, .evidence .lead"),
        new("helper/:id", "Your decision", "Block it, approve it, or call them first.", ".actionbar"),
        new("helper/:id", "Next: History", "Tap History.", ".appnav__item[href='/helper/history']", true),

        new("helper/history", "History", "Every decision is kept here.", "main h1"),
        new("helper/history", "Next: Messages", "Tap Messages.", ".appnav__item[href='/helper/messages']", true),

        new("helper/messages", "Messages", "Chats with the user and others.", ".msgs__list, main h1"),
        new("helper/messages", "Next: Settings", "Tap Settings.", ".appnav__item[href='/helper/settings']", true),

        new("helper/settings", "Settings", "The same reading and colour choices as the user.", "#set-read"),
        new("helper/settings", "Your access", "What you can and cannot see.", "#ha-title"),
    ];

    private readonly Dictionary<int, string> _seen = [];

    public TourSide Side { get; private set; } = TourSide.User;
    public bool Active { get; private set; } = true;
    public int Index { get; private set; }

    public IReadOnlyList<TourStep> Steps => Side == TourSide.User ? UserSteps : HelperSteps;
    public TourStep Current => Steps[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Steps.Count - 1;

    public event Action? Changed;

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
        Side = side;
        Index = 0;
        Active = true;
        _seen.Clear();
        Changed?.Invoke();
    }

    /// <summary>Puts the walk-around back where this browser tab left it.</summary>
    public void Restore(TourSide side, int index, bool active)
    {
        Side = side;
        Index = Math.Clamp(index, 0, Steps.Count - 1);
        Active = active;
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
