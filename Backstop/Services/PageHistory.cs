using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Backstop.Services;

/// <summary>Remembers the last page that was not Settings, so Settings can send the person back to where they were.</summary>
public sealed class PageHistory : IDisposable
{
    private readonly NavigationManager _nav;

    public PageHistory(NavigationManager nav)
    {
        _nav = nav;
        Note(_nav.Uri);
        _nav.LocationChanged += OnMoved;
    }

    /// <summary>Base-relative path to go back to. "/inbox" when nothing else has been seen.</summary>
    public string Back { get; private set; } = "/inbox";

    private void OnMoved(object? sender, LocationChangedEventArgs e) => Note(e.Location);

    private void Note(string uri)
    {
        var relative = _nav.ToBaseRelativePath(uri);
        var path = relative.Split('?', '#')[0];
        if (path.StartsWith("settings", StringComparison.OrdinalIgnoreCase)) return;
        Back = "/" + relative;
    }

    public void Dispose() => _nav.LocationChanged -= OnMoved;
}
