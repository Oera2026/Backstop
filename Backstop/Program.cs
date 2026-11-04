using System.Globalization;
using Backstop.Components;
using Backstop.Services;
using Backstop.Services.Api;
using Backstop.Services.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

// Dates read the British way ("Tuesday 27 October") whatever culture the server runs in.
var british = CultureInfo.GetCultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentCulture = british;
CultureInfo.DefaultThreadCurrentUICulture = british;

var builder = WebApplication.CreateBuilder(args);

// Load optional .env file from root or project dir if present (never tracked in git)
var rootEnv = Path.Combine(builder.Environment.ContentRootPath, "..", ".env");
if (File.Exists(rootEnv))
{
    foreach (var line in File.ReadAllLines(rootEnv))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var eq = trimmed.IndexOf('=');
        if (eq > 0)
        {
            var k = trimmed[..eq].Trim();
            var v = trimmed[(eq + 1)..].Trim();
            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0]) v = v[1..^1];
            Environment.SetEnvironmentVariable(k, v);
        }
    }
}
builder.Configuration.AddEnvironmentVariables();

// Hosts like Render give the port in PORT. Without it, local development uses 5106.
var portText = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(portText, out var port) && port is > 0 and < 65536)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://localhost:5106");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HTTP client for Meta AI Dev API scam checker (supports Muse models like muse-spark-1.3)
// One client, no redirects: a reply that tries to bounce the request (and the key) to another host is an error.
static SocketsHttpHandler NoRedirectHandler() => new()
{
    AllowAutoRedirect = false,
    UseCookies = false,
    MaxResponseHeadersLength = 16,
    ConnectTimeout = TimeSpan.FromSeconds(10),
};
// Meta can rate-limit us (429), fail (5xx) or go quiet. Retry twice with backoff (Retry-After is honoured), pause calls
// when most of them are failing, and cap each try. When all of that fails, callers fail closed: held, never cleared.
static void Resilience(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions o)
{
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.Delay = TimeSpan.FromSeconds(2);
    o.Retry.UseJitter = true;
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    o.CircuitBreaker.MinimumThroughput = 5;
    o.CircuitBreaker.FailureRatio = 0.6;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
}
// Stories are one long reply (about 40 seconds with painted scenes), so they get longer limits than the other Meta calls.
// One retry is the least the handler allows; it cannot finish inside the total, so a slow story is not asked for twice.
static void StoryResilience(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions o)
{
    o.Retry.MaxRetryAttempts = 1;
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(110);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(230);
    o.CircuitBreaker.MinimumThroughput = 5;
    o.CircuitBreaker.FailureRatio = 0.6;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
}
// One shared cap on calls to Meta (single instance, so in memory is enough). Retries are counted too (MetaRetryBudget,
// added after the resilience handler so it runs inside it, once per attempt).
builder.Services.AddSingleton<MetaCallGuard>();
builder.Services.AddTransient<MetaRetryBudget>();
static void MetaClient<T>(IServiceCollection services, Action<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions> resilience) where T : class
{
    var client = services.AddHttpClient<T>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
    client.AddStandardResilienceHandler(resilience);
    client.AddHttpMessageHandler<MetaRetryBudget>();
}
MetaClient<MetaAiScamChecker>(builder.Services, Resilience);
// Ask Me: the same Meta endpoint, the same rules.
MetaClient<MetaAssistant>(builder.Services, Resilience);
// Social stories: the same Meta endpoint, the same rules.
MetaClient<MetaStoryWriter>(builder.Services, StoryResilience);
// Explain and questions about a document: the same Meta endpoint, the same rules.
MetaClient<MetaDocumentExplainer>(builder.Services, Resilience);

// Link check (the public /try page only; real mail never opens a link). Optional blocklist lookups: Google Safe Browsing and URLhaus,
// skipped silently when their keys are empty. The fixed hosts are the only places those lookups go.
builder.Services.AddHttpClient<Backstop.Services.Links.LinkReputation>(c => c.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddTransient<Backstop.Services.Links.ILinkReputation>(sp => sp.GetRequiredService<Backstop.Services.Links.LinkReputation>());
builder.Services.AddSingleton<Backstop.Services.Links.ILinkInspector>(sp => new Backstop.Services.Links.LinkInspector(
    Backstop.Services.Links.LinkInspector.CreateClient(), null, sp.GetRequiredService<Backstop.Services.Links.ILinkReputation>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("LinkCheck")));

// Behind the host's load balancer: read the real client address and scheme from X-Forwarded-*.
// The app runs as a single instance behind one proxy, so the proxy lists are cleared.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = 1;
});
builder.Services.AddTransient<IAssistant>(sp => sp.GetRequiredService<MetaAssistant>());
builder.Services.AddTransient<IStoryWriter>(sp => sp.GetRequiredService<MetaStoryWriter>());
builder.Services.AddTransient<IDocumentExplainer>(sp => sp.GetRequiredService<MetaDocumentExplainer>());
builder.Services.AddTransient<IMetaScamChecker>(sp => sp.GetRequiredService<MetaAiScamChecker>());
builder.Services.AddTransient<IMetaDiagnostics>(sp => sp.GetRequiredService<MetaAiScamChecker>());

// Sign-in: a cookie holds the role (User, Helper or Demo). Accounts are made up and seeded (see AccountStore).
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "backstop.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<DemoTour>();

// Each sign-in costs a deliberately slow password hash, so one visitor gets 10 tries a minute. Over that, the page says to wait.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        ClientIdentity.Resolve(http, builder.Configuration["CLIENT_IP_HEADER"]) ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.Redirect("/login?error=wait");
        return ValueTask.CompletedTask;
    };
});

// MOCK: swap these registrations for real implementations later. No other code needs to change.
builder.Services.AddScoped<AskSession>();
builder.Services.AddSingleton<MailStore>();
builder.Services.AddSingleton<ISenderCheck, SenderCheck>();
builder.Services.AddSingleton<IMailService, MockMailService>();
builder.Services.AddSingleton<IGuardianService, MockGuardianService>();
builder.Services.AddHostedService<MailRescanService>();

// The Check, Show me, Explain, Remember and trusted-person modes.
builder.Services.AddSingleton<MockApiOptions>();
builder.Services.AddSingleton<IBackstopApi, MockBackstopApi>();
builder.Services.AddSingleton<IDemoControl, DemoControl>();

// The visitor's address, taken when a circuit opens, for the per-visitor cap on Meta calls.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, ClientIpCircuitHandler>();

#if DEBUG
if (args.Contains("--test-llama"))
{
    await Backstop.Tests.MetaScamCheckerTests.RunAllTests();
    return;
}
#endif

var app = builder.Build();

// First in the pipeline, so everything after it sees the real client address and https.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts(); // only sent on https requests (the host's proxy sets the scheme)
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Browser-side limits. Nothing the app shows can load script or content from anywhere else, and the app
// cannot be framed by another site (the demo panel frames itself, which is allowed).
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "media-src 'self' blob:; font-src 'self'; connect-src 'self' wss:; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'self'; frame-src 'self'";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "no-referrer";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
    await next();
});
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture("en-GB").AddSupportedCultures("en-GB").AddSupportedUICultures("en-GB");
    o.RequestCultureProviders.Clear();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();

// Profile pictures. MOCK: served from memory. The type comes from the sniffed bytes, never from the upload.
app.MapGet("/contact-photo/{id}", (string id, HttpContext http, IMailService mail) =>
{
    var photo = mail.GetContactPhoto(id);
    if (photo is null) return Results.NotFound();
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(photo.Bytes, photo.ContentType);
}).RequireAuthorization();

// Photos added in Show me. MOCK: served from memory, typed by the sniffed bytes.
app.MapGet("/mode-photo/{id}", (string id, HttpContext http, IBackstopApi api) =>
{
    var photo = api.GetPhoto(id);
    if (photo is null) return Results.NotFound();
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(photo.Bytes, photo.ContentType);
}).RequireAuthorization();

// Sign in with an email and password. Nobody is told which half was wrong.
app.MapPost("/account/login", async (HttpContext http, AccountStore accounts, IConfiguration config, [Microsoft.AspNetCore.Mvc.FromForm] string? email, [Microsoft.AspNetCore.Mvc.FromForm] string? password) =>
{
    var account = accounts.Verify(email, password, ClientIdentity.Resolve(http, config["CLIENT_IP_HEADER"]));
    if (account is null) return Results.Redirect("/login?error=1");
    await http.SignInAsync(AccountStore.Principal(account.DisplayName, account.Role));
    return Results.Redirect(account.Role == Roles.Helper ? "/helper" : "/");
}).RequireRateLimiting("login");

// Demo mode: one click, both sides, with the guided tour. Only two places can be asked for.
app.MapGet("/account/demo", async (HttpContext http, string? next, string? side) =>
{
    // Opening the demo without picking a side goes to the sign-in page, which offers the demo.
    if (next != "demo" && side is not ("user" or "helper")) return Results.Redirect("/login");
    await http.SignInAsync(AccountStore.Principal("Demo visitor", Roles.Demo));
    return Results.Redirect(next == "demo" ? "/demo" : side == "helper" ? "/helper?tour=1" : "/?tour=1");
});

app.MapGet("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
