namespace Backstop.Services.Accounts;

public enum TourSide { User, Helper }

public sealed record TourStep(TourSide Side, string Path, string Title, string Text);

/// <summary>Where a demo visitor is in the guided walk-around. One per browser tab (circuit).</summary>
public sealed class DemoTour
{
    public static readonly IReadOnlyList<TourStep> Steps =
    [
        new(TourSide.User, "/", "The inbox", "This is what the user sees. Mail that is still being checked is not shown here."),
        new(TourSide.User, "ask-me", "Ask Me", "The user can ask for help, check a message, or see how to do something."),
        new(TourSide.User, "check", "Check a message", "The user pastes in a message they are unsure about. Backstop checks it."),
        new(TourSide.User, "explain", "Explain something", "A letter or form goes in. Plain words come out."),
        new(TourSide.User, "remember", "Remember", "Saved notes for things the user wants to do again."),
        new(TourSide.Helper, "/helper", "Waiting for the helper", "Now you are the helper. Anything unclear waits here for a decision."),
        new(TourSide.Helper, "/helper/history", "History", "Every decision the helper made, in one list."),
        new(TourSide.Helper, "/helper/messages", "Messages", "The helper can talk to the user from here."),
        new(TourSide.User, "/demo", "Demo panel", "Send a made-up email and watch it get checked. Then try the user and helper side by side."),
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
    public void Restart() { Index = 0; Active = true; Changed?.Invoke(); }

    private readonly Dictionary<TourSide, int> _last = new() { [TourSide.User] = 0, [TourSide.Helper] = Steps.ToList().FindIndex(s => s.Side == TourSide.Helper) };

    private static string Norm(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();

    /// <summary>The visitor moved by hand: if that page is a step, the guide follows them.</summary>
    public void Follow(string relativePath)
    {
        var p = Norm(relativePath);
        var i = Steps.ToList().FindIndex(s => Norm(s.Path) == p);
        if (i < 0 || i == Index) return;
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
