using System.Globalization;
using Backstop.Components;
using Backstop.Services;
using Backstop.Services.Api;

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
builder.Services.AddHttpClient<MetaAiScamChecker>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
// Ask Me: the same Meta endpoint, the same rules.
builder.Services.AddHttpClient<MetaAssistant>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddTransient<IAssistant>(sp => sp.GetRequiredService<MetaAssistant>());
builder.Services.AddTransient<ILlamaScamChecker>(sp => sp.GetRequiredService<MetaAiScamChecker>());
builder.Services.AddTransient<IMetaScamChecker>(sp => sp.GetRequiredService<MetaAiScamChecker>());

// MOCK: swap these registrations for real implementations later. No other code needs to change.
builder.Services.AddScoped<AskSession>();
builder.Services.AddSingleton<MailStore>();
builder.Services.AddSingleton<IMailService, MockMailService>();
builder.Services.AddSingleton<IGuardianService, MockGuardianService>();

// The Check, Show me, Explain, Remember and trusted-person modes.
builder.Services.AddSingleton<MockApiOptions>();
builder.Services.AddSingleton<IBackstopApi, MockBackstopApi>();

if (args.Contains("--test-llama"))
{
    await Backstop.Tests.LlamaScamCheckerTests.RunAllTests();
    return;
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Browser-side limits. Nothing the app shows can load script or content from anywhere else, and the app
// cannot be framed by another site (the demo panel frames itself, which is allowed).
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "media-src 'self' blob:; font-src 'self'; connect-src 'self' ws: wss:; object-src 'none'; base-uri 'self'; " +
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

// Photos added in Show me. MOCK: served from memory, typed by the sniffed bytes.
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
