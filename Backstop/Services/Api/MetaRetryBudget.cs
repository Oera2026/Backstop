using System.Net;

namespace Backstop.Services.Api;

/// <summary>
/// Sits inside the retry handler, so it sees every attempt. The first attempt was already counted by the caller's
/// <see cref="MetaCallGuard.TryEnter(string?, out int, out int, out MetaCallGuard.Denial)"/>; each retry is counted here too,
/// so the daily cap counts real calls to Meta, not just checks. Over the cap, the retry is not sent: it answers
/// 429 Too Many Requests at once, and the caller fails closed as for any refusal.
/// </summary>
public sealed class MetaRetryBudget(MetaCallGuard guard) : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<bool> Sent = new("backstop.meta.sent");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(Sent, out var again) && again)
        {
            if (!guard.TryEnter(out _, out _))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request });
        }
        else request.Options.Set(Sent, true);
        return base.SendAsync(request, cancellationToken);
    }
}
