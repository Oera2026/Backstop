#if DEBUG
using Backstop.Components.Pages.Demo;
using Backstop.Components.Pages.Helper;
using Backstop.Components.Pages.User;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Backstop.Tests;

/// <summary>
/// Real pages, rendered to HTML with the real services and no AI key: what each person can actually see.
/// Clicks and browser behaviour are not covered here. Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class PageFlowTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => new(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => new(default(TValue)!);
    }

    private sealed class App : IAsyncDisposable
    {
        private readonly ServiceProvider _sp;
        private readonly HtmlRenderer _renderer;
        public IMailService Mail { get; }
        public IGuardianService Guardian { get; }
        public IBackstopApi Api { get; }

        public App()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging();
            services.AddSingleton<NavigationManager, FakeNav>();
            services.AddSingleton<IJSRuntime, NoJs>();
            services.AddSingleton<MailStore>();
            services.AddSingleton<ISenderCheck, SenderCheck>();
            services.AddSingleton<IMailService, MockMailService>();
            services.AddSingleton<IGuardianService, MockGuardianService>();
            services.AddSingleton<MockApiOptions>();
            services.AddSingleton<IBackstopApi, MockBackstopApi>();
            services.AddSingleton<IDemoControl, DemoControl>();
            services.AddScoped<AskSession>();
            services.AddScoped<PageHistory>();
            services.AddScoped<DemoTour>();
            _sp = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_sp.CreateScope().ServiceProvider, NullLoggerFactory.Instance);
            Mail = _sp.GetRequiredService<IMailService>();
            Guardian = _sp.GetRequiredService<IGuardianService>();
            Api = _sp.GetRequiredService<IBackstopApi>();
        }

        public Task<string> Render<T>(params (string Name, object? Value)[] parameters) where T : IComponent =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var values = parameters.ToDictionary(p => p.Name, p => p.Value);
                var output = await _renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(values));
                await output.QuiescenceTask;
                return output.ToHtmlString();
            });

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _sp.DisposeAsync();
        }
    }

    private const string HeldSubject = "Zebra held subject";
    private const string BlockedSubject = "Zebra blocked subject";
    private static readonly InboundMail HeldMail = new("Zebra Heldsender", "zebra.held@example.org", HeldSubject, "Please open https://zebra.example.net/pay today.");
    private static readonly InboundMail BlockedMail = new("Zebra Blocksender", "zebra.blocked@example.org", BlockedSubject, "Send the money now.");

    public static async Task RunAsync()
    {
        await using var app = new App();

        // No AI key: both stay held. Then the helper blocks one.
        var held = (await app.Mail.ReceiveAsync(HeldMail, false))!;
        var blocked = (await app.Mail.ReceiveAsync(BlockedMail, false))!;
        Assert(held.State == MessageState.Held && blocked.State == MessageState.Held, "No AI key: the messages are held");
        app.Guardian.Block(blocked.Id);

        // ---- The user's inbox: a count and nothing else ----
        var inbox = await app.Render<Inbox>();
        Assert(inbox.Contains("being checked by your helper"), "Inbox: the count of held mail is shown");
        Assert(!inbox.Contains(HeldSubject) && !inbox.Contains("Zebra Heldsender"), "Inbox: held mail shows no sender and no subject");
        Assert(!inbox.Contains(BlockedSubject) && !inbox.Contains("Zebra Blocksender"), "Inbox: blocked mail shows nothing");
        Assert(!inbox.Contains("blocked", StringComparison.OrdinalIgnoreCase), "Inbox: the user is never told mail was blocked");

        // ---- Opening a message by its address: only Cleared mail opens ----
        var heldPage = await app.Render<MessageView>(("Id", held.Id));
        Assert(heldPage.Contains("We cannot find that message") && !heldPage.Contains(HeldSubject), "Message page: held mail does not open");
        var blockedPage = await app.Render<MessageView>(("Id", blocked.Id));
        Assert(blockedPage.Contains("We cannot find that message") && !blockedPage.Contains(BlockedSubject), "Message page: blocked mail does not open");
        var cleared = app.Mail.GetInbox()[0];
        var clearedPage = await app.Render<MessageView>(("Id", cleared.Id));
        Assert(!clearedPage.Contains("We cannot find that message"), "Message page: cleared mail opens");

        // ---- Search: held and blocked mail never turn up ----
        var found = app.Mail.Search("Zebra");
        Assert(found.Messages.Count == 0, "Search: held and blocked mail are not found");

        // ---- The helper: sees held mail, with links defanged and never clickable ----
        var queue = await app.Render<Queue>();
        Assert(queue.Contains(HeldSubject), "Helper queue: held mail is listed");
        Assert(!queue.Contains(BlockedSubject), "Helper queue: blocked mail has left it");
        var evidence = await app.Render<Evidence>(("Id", held.Id));
        Assert(evidence.Contains(HeldSubject), "Evidence card: the helper sees the subject");
        Assert(!evidence.Contains("https://zebra.example.net") && !evidence.Contains("href=\"http"), "Evidence card: the link is not clickable");
        Assert(evidence.Contains("zebra[.]example[.]net"), "Evidence card: the link is defanged");

        // ---- The demo panel: the crash toggle starts off, and a visitor's email reaches no page ----
        var demo = await app.Render<DemoPanel>();
        Assert(demo.Contains("id=\"fail\"") && !System.Text.RegularExpressions.Regex.IsMatch(demo, "id=\"fail\"[^>]*checked"), "Demo panel: the crash toggle starts off");
        var own = new InboundMail("Sam", "sam@example.org", "Zebra own email", "See you at three.");
        Assert(await app.Mail.CheckOnceAsync(own, false) == MessageState.Held, "A visitor's email: no AI key is Held");
        Assert(!(await app.Render<Inbox>()).Contains("Zebra own email") && !(await app.Render<Queue>()).Contains("Zebra own email"), "A visitor's email: not in the inbox or the helper's queue");

        // ---- Explain asks only for what it can read ----
        Assert(!(await app.Render<Explain>()).Contains("PDF"), "Explain: PDFs are not offered");

        // ---- A page for something that has gone says so plainly ----
        Assert((await app.Render<CheckResultView>(("Id", "chk-gone"))).Contains("We cannot find that check"), "Check page: a disposed check says it cannot be found");
        Assert((await app.Render<StoryView>(("Id", "s-gone"))).Contains("We cannot find that story"), "Story page: a disposed story says it cannot be found");

        Console.WriteLine("PASS: Page flows: inbox, message, helper queue, evidence card and demo panel show each person only what they should.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
