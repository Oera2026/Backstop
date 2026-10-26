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

    protected override void OnInitialized() => Mail.Changed += OnStoreChanged;

    private void OnStoreChanged()
    {
        try { _ = InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { /* circuit already closed */ }
    }

    public virtual void Dispose() => Mail.Changed -= OnStoreChanged;
}
