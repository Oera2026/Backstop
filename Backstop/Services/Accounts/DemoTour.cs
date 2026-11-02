namespace Backstop.Services.Accounts;

public enum TourSide { User, Helper }

/// <summary>Selector is the thing to light up. Tap means the visitor taps it to move on (it is a link to the next step).</summary>
public sealed record TourStep(TourSide Side, string Path, string Title, string Text, string Selector, bool Tap = false);

/// <summary>Where a demo visitor is in the guided walk-around. One per browser tab (circuit).</summary>
public sealed class DemoTour
{
    public static readonly IReadOnlyList<TourStep> Steps =
    [
        new(TourSide.User, "/", "Inbox", "Mail waits here until it is checked. Tap Ask Me.", ".appnav__item[href='ask-me']", true),
        new(TourSide.User, "ask-me", "Ask Me", "Type a question and send it.", ".composer textarea"),
        new(TourSide.User, "ask-me", "Messages", "Chats with family live here. Tap Messages.", ".appnav__item[href='messages']", true),
        new(TourSide.User, "messages", "Notes", "Notes to do things again. Tap Remember.", ".appnav__item[href='remember']", true),
        new(TourSide.User, "remember", "Your notes", "Saved notes show here, with steps.", "main h1"),
        new(TourSide.Helper, "/helper", "Helper side", "Unclear mail waits here for a decision.", "main h1"),
        new(TourSide.Helper, "/helper", "History", "Past decisions are kept. Tap History.", ".appnav__item[href='/helper/history']", true),
        new(TourSide.Helper, "/helper/history", "Messages", "Talk to the user. Tap Messages.", ".appnav__item[href='/helper/messages']", true),
        new(TourSide.Helper, "/helper/messages", "Chat", "A private chat with the user.", "main h1"),
        new(TourSide.User, "/demo", "Demo panel", "Pick an email, then press Simulate.", "#scenario"),
    ];

    public bool Active { get; private set; } = true;
    public int Index { get; private set; }
    public TourStep Current => Steps[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Steps.Count - 1;

    public event Action? Changed;

    public void Next() { if (!IsLast) { Index++; _last[Current.Side] = Index; Changed?.Invoke(); } }
    public void Back() { if (!IsFirst) { Index--; _last[Current.Side] = Index; Changed?.Invoke(); } }
    public void Close() { Active = false; Changed?.Invoke(); }
    /// <summary>Starts again from the first step on the side the visitor is looking at.</summary>
    public void Restart(TourSide side)
    {
        Index = Steps.ToList().FindIndex(s => s.Side == side);
        _last[side] = Index;
        Active = true;
        Changed?.Invoke();
    }

    private readonly Dictionary<TourSide, int> _last = new() { [TourSide.User] = 0, [TourSide.Helper] = Steps.ToList().FindIndex(s => s.Side == TourSide.Helper) };

    private static string Norm(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();

    /// <summary>The visitor moved by hand: if that page is a step, the guide follows them.</summary>
    public void Follow(string relativePath)
    {
        var p = Norm(relativePath);
        var i = Steps.ToList().FindIndex(s => Norm(s.Path) == p);
        if (i < 0 || Norm(Current.Path) == p) return; // already on a step for this page
        Index = i;
        _last[Steps[i].Side] = i;
        Changed?.Invoke();
    }

    /// <summary>Jumps back to where the visitor last was on a side, for the User / Helper switch.</summary>
    public void GoToSide(TourSide side)
    {
        Index = _last[side];
        Changed?.Invoke();
    }
}
