namespace Backstop.Services.Accounts;

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

    /// <summary>The places the walk-around visits, in the order of the navigation bar.</summary>
    public static readonly IReadOnlyList<string> UserSections = ["Home", "Inbox", "Ask Me", "Messages", "Remember", "More"];
    public static readonly IReadOnlyList<string> HelperSections = ["Waiting", "Messages", "History", "Settings"];

    private static TourStep S(string section, string path, string title, string text, string selector, bool tap = false) =>
        new(path, title, text, selector, tap) { Section = section };

    // Each place starts on its own button in the navigation bar, then goes through what is on its pages.
    public static readonly IReadOnlyList<TourStep> UserSteps =
    [
        S("Home", "home", "Home", "Your start page. Big buttons for the things you do most.", ".appnav__item[href='home'], .site-header .brand"),
        S("Home", "home", "Choose one", "Tap a big button to go there.", ".modecards"),

        S("Inbox", "/", "Inbox", "Your mail. Safe mail shows here. Mail still being checked stays hidden.", ".appnav__item[href='/']"),
        S("Inbox", "/", "Write", "Start a new message here.", ".home__side .btn-primary"),
        S("Inbox", "/", "Shortcuts", "Contacts, sent mail, calendar and search.", ".tiles"),
        S("Inbox", "/", "A message", "Tap a message to read it.", ".home__list .row, .emptystate"),
        S("Inbox", "compose", "Write a message", "Say who it is for.", "#to"),
        S("Inbox", "compose", "The subject", "A few words about what it is.", "#subject"),
        S("Inbox", "compose", "Send", "Mail you send is checked before it goes.", ".form button[type='submit']"),
        S("Inbox", "contacts", "Contacts", "The people you write to. Tap a person to write to them.", ".rows .row, main h1"),
        S("Inbox", "sent", "Sent", "Messages you sent are kept here.", "main h1"),
        S("Inbox", "calendar", "Calendar", "Things that are coming up. Today has a mark.", ".day--today, main h1"),
        S("Inbox", "search", "Search", "Look for a message or a person.", "main h1"),

        S("Ask Me", "ask-me", "Ask Me", "Ask for help in plain words. Check a message, get help, or see how to do something.", ".appnav__item[href='ask-me']"),
        S("Ask Me", "ask-me", "Type here", "Type a question here.", ".composer textarea"),
        S("Ask Me", "ask-me", "Send", "Press Send. Ask Me answers in plain words.", ".composer button[type='submit']"),
        S("Ask Me", "check", "Check a message", "Not sure about a message? Paste it here.", "#check-text"),
        S("Ask Me", "check", "Check it", "Backstop looks for signs of a scam.", ".actions .btn-primary"),
        S("Ask Me", "explain", "Explain something", "Paste hard words. Get easy ones back.", "#explain-text"),
        S("Ask Me", "show-me", "Show me how", "Say what you want to do. You get steps.", "#goal"),
        S("Ask Me", "stories", "Stories", "A short story about something coming up.", "main h1"),
        S("Ask Me", "stories/new", "A new story", "Say what is coming up.", "#story-what"),

        S("Messages", "messages", "Messages", "Chats with the people you know.", ".appnav__item[href='messages']"),
        S("Messages", "messages", "A chat", "Tap one to open it.", ".rows .row"),

        S("Remember", "remember", "Remember", "Keeps how you did things before.", ".appnav__item[href='remember']"),
        S("Remember", "remember", "Ask", "Ask how you did something before.", ".askbox .askrow"),
        S("Remember", "remember", "Ideas", "Tap an idea if you are not sure what to ask.", ".chips"),

        S("More", "help", "Help", "Answers to common questions.", "#h-read"),
        S("More", "help", "Stuck?", "What to do if you are stuck.", "#h-stuck"),
        S("More", "settings", "Reading", "Make the words bigger. Slow the voice down.", "#set-read"),
        S("More", "settings", "Colours", "Light or dark. Stronger contrast. Less movement.", "#set-look"),
        S("More", "settings", "All done", "Press Done to go back.", ".settings-foot .btn-primary"),
    ];

    public static readonly IReadOnlyList<TourStep> HelperSteps =
    [
        S("Waiting", "/helper", "Waiting", "Mail the checker was unsure about waits here. The number is how many.", ".appnav__item[href='/helper']"),
        S("Waiting", "/helper", "Next up", "The most urgent message comes first.", ".hero, .rows a.row"),
        S("Waiting", "/helper", "Review it", "Tap Review it to see why it was held.", ".hero__go, .rows a.row", true),
        S("Waiting", "helper/:id", "Who sent it", "See who it is from and what it says.", ".evidence h1"),
        S("Waiting", "helper/:id", "The checker's view", "A short reason, in plain words.", ".evidence .lead"),
        S("Waiting", "helper/:id", "Warning signs", "Each thing that looked wrong is listed.", "#ev-flags, .evidence .lead"),
        S("Waiting", "helper/:id", "Your decision", "Block it, approve it, or call them first.", ".actionbar"),

        S("Messages", "helper/messages", "Messages", "Chats with the user and others.", ".appnav__item[href='/helper/messages']"),
        S("Messages", "helper/messages", "A chat", "Tap a person to read and reply.", ".msgs__list .row, .msgs__list, main h1"),

        S("History", "helper/history", "History", "Every decision is kept here.", ".appnav__item[href='/helper/history']"),
        S("History", "helper/history", "Past decisions", "What was approved, blocked or called about.", ".rows .row, .rows"),

        S("Settings", "helper/settings", "Settings", "The same reading and colour choices as the user.", ".appnav__item[href='/helper/settings']"),
        S("Settings", "helper/settings", "Reading", "Make the words bigger.", "#set-read"),
        S("Settings", "helper/settings", "Your access", "What you can and cannot see.", "#ha-title"),
    ];

    private readonly Dictionary<int, string> _seen = [];

    public TourSide Side { get; private set; } = TourSide.User;
    public bool Active { get; private set; } = true;
    public int Index { get; private set; }

    public IReadOnlyList<string> Sections => Side == TourSide.User ? UserSections : HelperSections;
    public IReadOnlyList<TourStep> Steps => Side == TourSide.User ? UserSteps : HelperSteps;
    public TourStep Current => Steps[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Steps.Count - 1;

    /// <summary>True once the guide has started for this visit. A new load of the app is a new visit.</summary>
    public bool Begun { get; private set; }

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

    public void Begin(TourSide side) { Begun = true; Start(side); }

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
