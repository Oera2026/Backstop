using Backstop.Models;

namespace Backstop.Services;

/// <summary>One line of the Ask Me conversation, as shown on screen.</summary>
public sealed record AskLine(bool FromUser, string Text, bool HadPicture = false, Verdict? Verdict = null,
                             IReadOnlyList<string>? LinesOrNull = null, string? Href = null, string? LinkText = null,
                             AskDraft? Draft = null, AskEvent? Event = null, IReadOnlyList<AskLink>? Found = null, FailureInfo? Failure = null, Story? Story = null)
{
    public IReadOnlyList<string> Lines => LinesOrNull ?? [];
}

/// <summary>
/// The Ask Me conversation for one person's browser tab (scoped per circuit). The Ask Me page and the dock on the
/// right both show this same conversation, so moving between them never loses what was said.
/// </summary>
public sealed class AskSession
{
    public List<AskLine> Turns { get; } = [];

    /// <summary>Whether the dock on the right is open.</summary>
    public bool DockOpen { get; private set; }

    /// <summary>What the person is looking at, as the page that shows it reports it.</summary>
    public sealed record PageContext(string? MessageId, string? DraftTo = null, string? DraftSubject = null, string? DraftBody = null);

    private object? _owner;
    private Func<PageContext>? _page;

    /// <summary>The page on screen says what it shows. Read when a question is sent, not on every key press.</summary>
    public void SetPage(object owner, Func<PageContext> page) { _owner = owner; _page = page; }

    /// <summary>Only the page that set the context can clear it, so a page that closes late cannot wipe the next one.</summary>
    public void ClearPage(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;
        _owner = null;
        _page = null;
    }

    public PageContext? Page => _page?.Invoke();

    public event Action? Changed;

    public void SetDock(bool open)
    {
        if (DockOpen == open) return;
        DockOpen = open;
        Changed?.Invoke();
    }

    public void Touch() => Changed?.Invoke();
}
