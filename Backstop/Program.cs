using Backstop.Components;
using Backstop.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MOCK: swap these registrations for real implementations later. No other code needs to change.
builder.Services.AddSingleton<MailStore>();
builder.Services.AddSingleton<IMailService, MockMailService>();
builder.Services.AddSingleton<IGuardianService, MockGuardianService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
