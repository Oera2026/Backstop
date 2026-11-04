using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Backstop.Services.Api;

/// <summary>
/// The typed clients for Meta. The resilience handler owns every time limit, so HttpClient's own 100 second default is switched off:
/// without that, a long story or a raised META_AI_TIMEOUT_SECONDS would be cut off at 100 seconds whatever the settings said.
/// Meta can rate-limit us (429), fail (5xx) or go quiet. A try is retried once with backoff (Retry-After is honoured), calls pause
/// when most of them are failing, and each try has a cap. When all of that fails, callers fail closed: held, never cleared.
/// </summary>
public static class MetaClients
{
    public static void Add(IServiceCollection services, IConfiguration config)
    {
        var options = MetaAiOptions.Load(config);

        // One shared cap on calls to Meta (single instance, so in memory is enough). Retries are counted too (MetaRetryBudget,
        // added after the resilience handler so it runs inside it, once per attempt).
        services.AddSingleton<MetaCallGuard>();
        services.AddTransient<MetaRetryBudget>();

        Client<MetaAiScamChecker>(services, o => Standard(o, options));
        Client<MetaAssistant>(services, o => Standard(o, options));
        Client<MetaStoryWriter>(services, o => Story(o, options));
        Client<MetaDocumentExplainer>(services, o => Standard(o, options));
        Client<MetaGuideWriter>(services, o => Standard(o, options));
        Client<MetaMemoryAnswerer>(services, o => Standard(o, options));
    }

    /// <summary>
    /// One client, no redirects: a reply that tries to bounce the request (and the key) to another host is an error.
    /// These clients live inside singletons, so the factory's handler rotation never reaches them. Connections are renewed here instead, so a DNS change is picked up.
    /// </summary>
    internal static SocketsHttpHandler NoRedirectHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        MaxResponseHeadersLength = 16,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private static void Client<T>(IServiceCollection services, Action<HttpStandardResilienceOptions> resilience) where T : class
    {
        var client = services.AddHttpClient<T>(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(NoRedirectHandler)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        client.AddStandardResilienceHandler(resilience);
        client.AddHttpMessageHandler<MetaRetryBudget>();
    }

    // Muse models reason before they answer, so one reply takes 6 to 30 seconds. A try gets META_AI_TIMEOUT_SECONDS. The whole call
    // is longer than a try (MetaAiOptions.CallBudget), so a slow reply really does get its second try.
    internal static void Standard(HttpStandardResilienceOptions o, MetaAiOptions options)
    {
        o.Retry.MaxRetryAttempts = 1;
        o.Retry.Delay = TimeSpan.FromSeconds(1);
        o.Retry.UseJitter = true;
        o.AttemptTimeout.Timeout = options.AttemptTimeout;
        o.TotalRequestTimeout.Timeout = options.CallBudget;
        Breaker(o, options.AttemptTimeout);
    }

    // Stories are one long reply (about 40 seconds for a short story with painted scenes, longer for up to 12 pages), so they get longer limits than the other Meta calls.
    // One retry is the least the handler allows; it cannot finish inside the total, so a slow story is not asked for twice.
    internal static void Story(HttpStandardResilienceOptions o, MetaAiOptions options)
    {
        var attempt = TimeSpan.FromSeconds(Math.Max(190, options.AttemptTimeout.TotalSeconds + 10));
        o.Retry.MaxRetryAttempts = 1;
        o.AttemptTimeout.Timeout = attempt;
        o.TotalRequestTimeout.Timeout = attempt + TimeSpan.FromSeconds(10);
        Breaker(o, attempt);
    }

    // The handler refuses a sampling window shorter than two tries.
    private static void Breaker(HttpStandardResilienceOptions o, TimeSpan attempt)
    {
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(120, attempt.TotalSeconds * 2 + 1));
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.FailureRatio = 0.6;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
    }
}
