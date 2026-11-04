#if DEBUG
#pragma warning disable BL0006
using Backstop.Components.Layout;
using Backstop.Components.Pages.Helper;
using Backstop.Components.Pages.User;
using Backstop.Components.Shared;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Backstop.Tests;

/// <summary>
/// Accessibility and display rules that can be checked without a browser: links in subjects and senders, the skip link and
/// error bar in the layouts, and the "Read this to me" button. Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class AccessibilityTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class FakeJs : IJSRuntime
    {
        public bool Speaks = true;
        public List<string> Calls { get; } = [];
        public List<object?[]?> Args { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            Args.Add(args);
            object? result = identifier == "backstop.speakWithEnd" ? Speaks : default(TValue);
            return new((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Host(IServiceProvider sp) : Renderer(sp, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new Exception("Render failed", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        public async Task<(IComponent Component, int Id)> Mount<T>(params (string Name, object? Value)[] parameters) where T : IComponent =>
            await Dispatcher.InvokeAsync(async () =>
            {
                var c = InstantiateComponent(typeof(T));
                var id = AssignRootComponentId(c);
                await RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters.ToDictionary(p => p.Name, p => p.Value)));
                return (c, id);
            });

        public Task Set(int id, string name, object? value) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?> { [name] = value })));

        public Task Remove() => Dispatcher.InvokeAsync(async () => await DisposeAsync());

        public List<RenderTreeFrame> Tree(int id)
        {
            var r = GetCurrentRenderTreeFrames(id);
            return r.Array.Take(r.Count).ToList();
        }

        public async Task Click(int id)
        {
            var handler = Tree(id).First(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "onclick").AttributeEventHandlerId;
            await Dispatcher.InvokeAsync(() => DispatchEventAsync(handler, null, new MouseEventArgs()));
        }
    }

    private static ServiceProvider Services(FakeJs js)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton<IJSRuntime>(js);
        services.AddSingleton<MailStore>();
        services.AddSingleton<ISenderCheck, SenderCheck>();
        services.AddSingleton<IMailService, MockMailService>();
        services.AddSingleton<IGuardianService, MockGuardianService>();
        services.AddSingleton<MockApiOptions>();
        services.AddSingleton<IBackstopApi, MockBackstopApi>();
        services.AddScoped<AskSession>();
        return services.BuildServiceProvider();
    }

    private static Task<string> Html<T>(HtmlRenderer r, params (string Name, object? Value)[] parameters) where T : IComponent =>
        r.Dispatcher.InvokeAsync(async () =>
        {
            var output = await r.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters.ToDictionary(p => p.Name, p => p.Value)));
            await output.QuiescenceTask;
            return output.ToHtmlString();
        });

    private const string Raw = "https://zebra.example.net/pay.html";
    private static readonly InboundMail LinkMail = new($"Bank {Raw}", "billing@example.org", $"Pay at {Raw} today", "Hello.");

    public static async Task RunAsync()
    {
        // ---- Cleaning text twice changes nothing, so the service layer and the page can both do it ----
        foreach (var s in new[] { Raw, "See www.example.org/a.html now", "bit.ly/xyz", "Tea at three", "http://a.example.com:8080/x?y=1.2", LinkMail.Subject })
        {
            Assert(Defang.ForHelper(Defang.ForHelper(s)) == Defang.ForHelper(s), $"Defanging twice is harmless: {s}");
            Assert(Defang.ForUser(Defang.ForUser(s)) == Defang.ForUser(s), $"Taking links out twice is harmless: {s}");
        }

        await using var sp = Services(new FakeJs());
        await using var html = new HtmlRenderer(sp.CreateScope().ServiceProvider, NullLoggerFactory.Instance);
        var store = sp.GetRequiredService<MailStore>();

        var cleared = MockData.CreateMessage(LinkMail);
        cleared.State = MessageState.Cleared;
        store.Add(cleared);
        var held = MockData.CreateMessage(LinkMail);
        held.State = MessageState.Held;
        store.Add(held);

        // ---- Links in subjects and senders are never shown as links ----
        var inbox = await Html<Inbox>(html);
        Assert(!inbox.Contains("zebra.example.net") && inbox.Contains("[link removed]"), "Inbox: a link in a subject or sender is taken out");
        var message = await Html<MessageView>(html, ("Id", cleared.Id));
        Assert(!message.Contains("zebra.example.net") && message.Contains("[link removed]"), "Message page: a link in a subject or sender is taken out");
        var evidence = await Html<Evidence>(html, ("Id", held.Id));
        var evidenceHead = evidence[..evidence.IndexOf("</code>", StringComparison.Ordinal)];
        Assert(!evidenceHead.Contains("zebra.example.net") && evidenceHead.Contains("zebra[.]example[.]net"), "Evidence card: a link in a subject or sender is defanged");

        // ---- Layouts: the skip link stays on the page, and the error bar is announced with a real Reload button ----
        RenderFragment body = b => b.AddContent(0, "Page");
        foreach (var layout in new[]
        {
            await Html<TryLayout>(html, ("Body", body)),
            await Html<WelcomeLayout>(html, ("Body", body)),
            await Html<DemoLayout>(html, ("Body", body)),
        })
        {
            Assert(layout.Contains("class=\"skip-link\" href=\"#main\"") && layout.Contains("Skip to the page"), "Layout: the skip link says where it goes");
            Assert(layout.Contains("role=\"alert\"") && layout.Contains("reload") && !layout.Contains("<a href=\".\""), "Layout: the error bar is an alert with a Reload button");
        }

        // ---- A result is announced ----
        var verdict = await Html<VerdictCard>(html, ("Verdict", Verdict.LooksSafe), ("Reason", "Fine."));
        Assert(verdict.Contains("role=\"status\""), "Verdict: the result is announced");

        // ---- Read this to me ----
        {
            var js = new FakeJs();
            await using var buttonSp = Services(js);
            var host = new Host(buttonSp);
            var (_, id) = await host.Mount<ReadAloudButton>(("Text", "Step 1. Load it."));
            bool Pressed() => host.Tree(id).Any(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "aria-pressed" && (f.AttributeValue as string) == "true");
            string Note() => string.Concat(host.Tree(id).Where(f => f.FrameType == RenderTreeFrameType.Text).Select(f => f.TextContent));

            await host.Click(id);
            Assert(Pressed() && js.Calls.Contains("backstop.speakWithEnd"), "Read this to me: pressing it starts the voice");
            await host.Set(id, "Text", "Step 2. Press Start.");
            Assert(!Pressed() && js.Calls.Contains("backstop.stop"), "Read this to me: the next step stops the old words");

            js.Speaks = false;
            await host.Click(id);
            var frames = host.Tree(id);
            Assert(frames.Any(f => f.FrameType == RenderTreeFrameType.Attribute && (f.AttributeValue as string) == "readaloud-note") && !frames.Any(f => (f.FrameType == RenderTreeFrameType.Attribute) && (f.AttributeValue as string) == "sr-note"), "Read this to me: the note is on show, not hidden");
            Assert(Note().Contains("cannot read out loud"), "Read this to me: a device with no voice says so");

            await host.Set(id, "Text", "");
            Assert(host.Tree(id).All(f => f.FrameType != RenderTreeFrameType.Element), "Read this to me: nothing to read, no button");
            await host.Remove();
        }

        // ---- A dialog takes focus when it opens and gives it back when it goes ----
        {
            var js = new FakeJs();
            await using var modalSp = Services(js);
            var host = new Host(modalSp);
            await host.Mount<ModalFocus>(("Root", ".storytheatre"), ("Focus", ".storytheatre__exit"));
            Assert(js.Calls.Contains("backstop.modalOpen") && (string?)js.Args[js.Calls.IndexOf("backstop.modalOpen")]![0] == ".storytheatre", "Dialog: it takes focus when it opens");
            await host.Remove();
            Assert(js.Calls.Contains("backstop.modalClose"), "Dialog: focus goes back when it closes");
        }

        Console.WriteLine("PASS: Accessibility: links in subjects and senders, skip link, error bar, read aloud and dialog focus.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
