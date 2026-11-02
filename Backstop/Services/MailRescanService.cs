namespace Backstop.Services;

/// <summary>
/// Mail the AI could not check stays held. This tries again on a timer, so it is released, flagged or blocked as
/// soon as the AI can answer. Nothing leaves the held state any other way (a helper can still block it).
/// </summary>
public sealed class MailRescanService(IMailService mail, ILogger<MailRescanService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await mail.RescanAsync(); }
                catch (Exception ex) { logger.LogWarning("Rescan of held mail failed: {Type}", ex.GetType().Name); } // no message content in logs
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
