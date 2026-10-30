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
            Environment.SetEnvironmentVariable(k, v);
        }
    }
}
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HTTP client for Meta's Llama AI scam checker
builder.Services.AddHttpClient<ILlamaScamChecker, LlamaScamChecker>();

// MOCK: swap these registrations for real implementations later. No other code needs to change.
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
