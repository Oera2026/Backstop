using Backstop.Services.Api;

namespace Backstop.Services;

/// <summary>Deletes what visitors sent to Meta AI once its time is up. Logs no content.</summary>
public sealed class VisitorDataDisposalService(IBackstopApi api, ILogger<VisitorDataDisposalService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { api.DisposeExpiredVisitorData(); }
                catch (Exception ex) { logger.LogWarning("Disposal of visitor data failed: {Type}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
