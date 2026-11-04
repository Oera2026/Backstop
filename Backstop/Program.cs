using System.Globalization;
using Backstop.Components;
using Backstop.Services;
using Backstop.Services.Api;

// Dates read the British way ("Tuesday 27 October") whatever culture the server runs in.
var british = CultureInfo.GetCultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentCulture = british;
CultureInfo.DefaultThreadCurrentUICulture = british;

var builder = WebApplication.CreateBuilder(args);

// Load the optional .env file from the repo root if present (never tracked in git). A value already set in the real environment wins.
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
            if (Environment.GetEnvironmentVariable(k) is null) Environment.SetEnvironmentVariable(k, v);
        }
    }
}
builder.Configuration.AddEnvironmentVariables();

// Hosts like Render give the port in PORT. Without it, local development uses 5106. An address given with --urls wins over both.
if (string.IsNullOrWhiteSpace(new ConfigurationBuilder().AddCommandLine(args).Build()["urls"]))
{
    var portText = Environment.GetEnvironmentVariable("PORT");
    if (int.TryParse(portText, out var port) && port is > 0 and < 65536)
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        builder.WebHost.UseUrls("http://localhost:5106");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The clients for the Meta AI Dev API (Muse models like muse-spark-1.3), with their time limits and caps.
MetaClients.Add(builder.Services, builder.Configuration);

// Link check (the public /try page only; real mail never opens a link). Optional blocklist lookups: Google Safe Browsing and URLhaus,
// skipped silently when their keys are empty. The fixed hosts are the only places those lookups go.
builder.Services.AddHttpClient<Backstop.Services.Links.LinkReputation>(c => c.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(MetaClients.NoRedirectHandler);
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
builder.Services.AddTransient<IGuideWriter>(sp => sp.GetRequiredService<MetaGuideWriter>());
builder.Services.AddTransient<IMemoryAnswerer>(sp => sp.GetRequiredService<MetaMemoryAnswerer>());
builder.Services.AddTransient<IMetaScamChecker>(sp => sp.GetRequiredService<MetaAiScamChecker>());
builder.Services.AddTransient<IMetaDiagnostics>(sp => sp.GetRequiredService<MetaAiScamChecker>());

builder.Services.AddScoped<DemoTour>();

// MOCK: swap these registrations for real implementations later. No other code needs to change.
builder.Services.AddScoped<AskSession>();
builder.Services.AddScoped<PageHistory>();
builder.Services.AddSingleton<MailStore>();
builder.Services.AddSingleton<ISenderCheck, SenderCheck>();
builder.Services.AddSingleton<IMailService, MockMailService>();
builder.Services.AddSingleton<IGuardianService, MockGuardianService>();
builder.Services.AddHostedService<MailRescanService>();

// The Check, Show me, Explain, Remember and trusted-person modes.
builder.Services.AddSingleton<MockApiOptions>();
builder.Services.AddSingleton<IBackstopApi, MockBackstopApi>();
builder.Services.AddHostedService<VisitorDataDisposalService>();
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
});

// The user's and helper's profile pictures. MOCK: served from memory, typed by the sniffed bytes.
app.MapGet("/mode-photo/{id}", (string id, HttpContext http, IBackstopApi api) =>
{
    var photo = api.GetPhoto(id);
    if (photo is null) return Results.NotFound();
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(photo.Bytes, photo.ContentType);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
