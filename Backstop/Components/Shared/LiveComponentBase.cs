using Backstop.Services;
using Microsoft.AspNetCore.Components;

namespace Backstop.Components.Shared;

/// <summary>
/// Base for pages that must update live when shared state changes (for example
/// the helper approves a message while the user is looking at the inbox).
/// </summary>
public abstract class LiveComponentBase : ComponentBase, IDisposable
{
    [Inject] protected IMailService Mail { get; set; } = default!;
    [Inject] protected IGuardianService Guardian { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    /// <summary>Dates and times in the reader's own time zone (cascaded from Routes).</summary>
    [CascadingParameter] protected UserClock Clock { get; set; } = UserClock.Server;

    protected override void OnInitialized() => Mail.Changed += OnStoreChanged;

    private void OnStoreChanged()
    {
        try
        {
            _ = InvokeAsync(async () =>
            {
                try { await OnStoreChangedAsync(); }
                catch (Services.Api.BackstopApiException) { /* keep what is on screen */ }
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { /* circuit already closed */ }
    }

    /// <summary>Reload anything the screen fetched when mail or helper state changes. The default does nothing.</summary>
    protected virtual Task OnStoreChangedAsync() => Task.CompletedTask;

    public virtual void Dispose() => Mail.Changed -= OnStoreChanged;
}
