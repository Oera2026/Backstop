using Backstop.Services.Api;
using Microsoft.AspNetCore.Components;

namespace Backstop.Components.Shared;

/// <summary>
/// Base for the Check, Show me, Explain, Remember and trusted-person screens. Adds the mode API and
/// reloads quietly when shared data changes (for example the trusted person releases a checked message).
/// </summary>
public abstract class ModeComponentBase : LiveComponentBase
{
    [Inject] protected IBackstopApi Api { get; set; } = default!;

    protected string UserName => Api.Household.UserName;
    protected string Helper => Api.Household.TrustedPersonName;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Api.Changed += OnApiChanged;
    }

    private void OnApiChanged() => _ = InvokeAsync(async () =>
    {
        try
        {
            await OnApiChangedAsync();
            StateHasChanged();
        }
        catch (BackstopApiException) { /* keep what is on screen; the next action shows the error */ }
        catch (ObjectDisposedException) { /* circuit already closed */ }
    });

    /// <summary>Reload what the screen shows. Called after any change; the default does nothing.</summary>
    protected virtual Task OnApiChangedAsync() => Task.CompletedTask;

    public override void Dispose()
    {
        Api.Changed -= OnApiChanged;
        base.Dispose();
    }
}
