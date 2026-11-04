#if DEBUG
#pragma warning disable BL0006
using System.Reflection;
using Backstop.Components.Helper;
using Backstop.Components.Pages.Demo;
using Backstop.Components.Pages.Helper;
using Backstop.Components.Pages.User;
using Backstop.Components.Shared;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Backstop.Tests;

/// <summary>
/// Page behaviour with real components, real services and real clicks: double tap, undo, changing story, refused runs,
/// Check again, links that change, uploads that fail and the camera. No browser, so the browser's own part is not covered.
/// Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class PageBehaviourTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav(string path) => Initialize("http://localhost/", "http://localhost/" + path);
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }
        protected override void SetNavigationLockState(bool value) { }
    }

    private sealed class FakeJs : IJSRuntime
    {
        public Func<string, Type, object?>? Answer;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var result = Answer?.Invoke(identifier, typeof(TValue));
            return new ValueTask<TValue>(result is null ? default(TValue)! : (TValue)result);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    // A checker, writers and an explainer that all wait at one gate, so a second tap arrives while the first is still working.
    private sealed class Stubs : IMetaScamChecker, IStoryWriter, IGuideWriter, IDocumentExplainer
    {
        public bool Fail;
        public int Calls;
        public TaskCompletionSource? Gate;

        private async Task Enter()
        {
            Calls++;
            if (Gate is { } gate) await gate.Task;
        }

        public async Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            await Enter();
            return Fail ? new MetaScamAnalysis(Verdict.BeCareful, "No answer.", [], false, "down") : new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true);
        }

        public async Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default)
        {
            await Enter();
            var count = situation.Contains("long") ? 7 : 2;
            return new StoryDraft(true, situation, [.. Enumerable.Range(1, count).Select(i => new StoryPage($"Part {i}", "It goes on.", "bus"))], false);
        }

        public async Task<GuideDraft> WriteAsync(string goal, byte[]? photo, string? mimeType, CancellationToken ct = default)
        {
            await Enter();
            return new GuideDraft(true, false, "A guide", ["One.", "Two."], null, "washer", ["home"], "did it");
        }

        public async Task<DocumentReading> ExplainAsync(string? text, byte[]? image, string? mimeType, ReadingLevel level, ExplainFormat format, CancellationToken ct = default)
        {
            await Enter();
            return new DocumentReading(true, "letter", "A letter", "Overview.", null, [], [], [], "The words.", false);
        }

        public Task<DocumentAnswer> AnswerAsync(string documentText, string question, CancellationToken ct = default) =>
            Task.FromResult(new DocumentAnswer(true, "Yes.", false));
    }

    // A real browser takes a moment to say a page change arrived, and the old buttons still work until it does.
    // Holding that answer back is what lets a second tap land on the same button, exactly as it can on a slow phone.
    private sealed class Host : Renderer
    {
        private TaskCompletionSource? _browser;
        private readonly Dictionary<int, IComponent> _mounted = [];
        public object Component(int id) => _mounted[id];
        public Host(IServiceProvider sp) : base(sp, NullLoggerFactory.Instance) { }
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new Exception("Render failed", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => _browser?.Task ?? Task.CompletedTask;

        public void HoldBrowser() => _browser = new TaskCompletionSource();
        public void ReleaseBrowser()
        {
            var held = _browser;
            _browser = null;
            held?.SetResult();
        }

        public Task<int> Mount<T>(params (string Name, object? Value)[] parameters) where T : IComponent =>
            Dispatcher.InvokeAsync(async () =>
            {
                var c = InstantiateComponent(typeof(T));
                var id = AssignRootComponentId(c);
                _mounted[id] = c;
                await RenderRootComponentAsync(id, Params(parameters));
                return id;
            });

        public Task Set(int id, params (string Name, object? Value)[] parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id, Params(parameters)));

        private static ParameterView Params((string Name, object? Value)[] parameters) =>
            ParameterView.FromDictionary(parameters.ToDictionary(p => p.Name, p => p.Value));

        private List<RenderTreeFrame> Tree(int id)
        {
            var r = GetCurrentRenderTreeFrames(id);
            return r.Array.Take(r.Count).ToList();
        }

        public string Text(int id) =>
            string.Concat(Tree(id).Where(f => f.FrameType is RenderTreeFrameType.Text or RenderTreeFrameType.Markup)
                .Select(f => f.FrameType == RenderTreeFrameType.Text ? f.TextContent : f.MarkupContent));

        // The handler on the element that holds these words, or the first element that has this event.
        private ulong Handler(int id, string element, string? words, string attribute)
        {
            var tree = Tree(id);
            for (var i = 0; i < tree.Count; i++)
            {
                var f = tree[i];
                if (f.FrameType != RenderTreeFrameType.Element || f.ElementName != element) continue;
                var inside = tree.Skip(i).Take(f.ElementSubtreeLength).ToList();
                var said = string.Join(" ", inside.Where(x => x.FrameType == RenderTreeFrameType.Text).Select(x => x.TextContent));
                if (words is not null && !said.Contains(words)) continue;
                return inside.First(x => x.FrameType == RenderTreeFrameType.Attribute && x.AttributeName == attribute).AttributeEventHandlerId;
            }
            throw new Exception($"No {element} with \"{words}\". The page says: {Text(id)}");
        }

        public string? Field(int id, string elementId)
        {
            var tree = Tree(id);
            for (var i = 0; i < tree.Count; i++)
            {
                if (tree[i].FrameType != RenderTreeFrameType.Element) continue;
                var inside = tree.Skip(i + 1).TakeWhile(x => x.FrameType == RenderTreeFrameType.Attribute).ToList();
                if (inside.Any(a => a.AttributeName == "id" && Equals(a.AttributeValue, elementId)))
                    return inside.FirstOrDefault(a => a.AttributeName == "value").AttributeValue?.ToString();
            }
            throw new Exception($"No field {elementId}");
        }

        public Task Click(int id, string words) => Fire(Handler(id, "button", words, "onclick"), new MouseEventArgs());
        public Task Submit(int id) => Fire(Handler(id, "form", null, "onsubmit"), EventArgs.Empty);

        /// <summary>Two taps in one moment, before the page can change. Returns once both have been handled as far as they go.</summary>
        public async Task<Task> DoubleTap(int id, string element, string? words, string attribute)
        {
            var handler = Handler(id, element, words, attribute);
            EventArgs args = attribute == "onclick" ? new MouseEventArgs() : EventArgs.Empty;
            HoldBrowser();
            var both = await Dispatcher.InvokeAsync(() => Task.FromResult(Task.WhenAll(DispatchEventAsync(handler, null, args), DispatchEventAsync(handler, null, args))));
            ReleaseBrowser();
            return both;
        }

        public Task Change(int id, string elementId, string value)
        {
            var tree = Tree(id);
            for (var i = 0; i < tree.Count; i++)
            {
                if (tree[i].FrameType != RenderTreeFrameType.Element) continue;
                var inside = tree.Skip(i + 1).TakeWhile(x => x.FrameType == RenderTreeFrameType.Attribute).ToList();
                if (inside.Any(a => a.AttributeName == "id" && Equals(a.AttributeValue, elementId)))
                    return Fire(inside.First(a => a.AttributeName == "onchange").AttributeEventHandlerId, new ChangeEventArgs { Value = value });
            }
            throw new Exception($"No field {elementId}");
        }

        private Task Fire(ulong handler, EventArgs args) => Dispatcher.InvokeAsync(() => DispatchEventAsync(handler, null, args));
    }

    private sealed class App : IAsyncDisposable
    {
        private readonly ServiceProvider _sp;
        public Host Host { get; }
        public Stubs Ai { get; } = new();
        public FakeJs Js { get; } = new();
        public FakeNav Nav { get; }
        public IMailService Mail { get; }
        public IGuardianService Guardian { get; }
        public IBackstopApi Api { get; }
        public MailStore Store { get; }
        public DemoTour Tour { get; }

        public App(string path = "")
        {
            Nav = new FakeNav(path);
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging();
            services.AddSupplyValueFromQueryProvider();
            services.AddSingleton<NavigationManager>(Nav);
            services.AddSingleton<IJSRuntime>(Js);
            services.AddSingleton<MailStore>();
            services.AddSingleton<ISenderCheck, SenderCheck>();
            services.AddSingleton<IMetaScamChecker>(Ai);
            services.AddSingleton<IStoryWriter>(Ai);
            services.AddSingleton<IGuideWriter>(Ai);
            services.AddSingleton<IDocumentExplainer>(Ai);
            services.AddSingleton<IMailService, MockMailService>();
            services.AddSingleton<IGuardianService, MockGuardianService>();
            services.AddSingleton<MockApiOptions>();
            services.AddSingleton<IBackstopApi, MockBackstopApi>();
            services.AddSingleton<IDemoControl, DemoControl>();
            services.AddSingleton<AskSession>();
            services.AddSingleton<PageHistory>();
            services.AddSingleton<DemoTour>();
            _sp = services.BuildServiceProvider();
            Host = new Host(_sp);
            Mail = _sp.GetRequiredService<IMailService>();
            Guardian = _sp.GetRequiredService<IGuardianService>();
            Api = _sp.GetRequiredService<IBackstopApi>();
            Store = _sp.GetRequiredService<MailStore>();
            Tour = _sp.GetRequiredService<DemoTour>();
        }

        public Task Go(string path) => Host.Dispatcher.InvokeAsync(() => Nav.NavigateTo(path));

        public async ValueTask DisposeAsync()
        {
            await Host.Dispatcher.InvokeAsync(() => Host.DisposeAsync().AsTask());
            await _sp.DisposeAsync();
        }
    }

    private const string Draft = "compose?to=sam%40example.org&subject=Tea&body=See%20you%20at%20three.";
    private static int SentTea(App app) => app.Mail.GetSent().Count(s => s.Subject == "Tea");

    public static async Task RunAsync()
    {
        await Task.WhenAll(ComposeUndoStopsTheSend(), ComposeSendsOnce());
        await ComposeSaysTheRealReason();
        await StoryChangeStartsAtItsFirstPage();
        await DemoPanelSaysWhenARunIsRefused();
        await EvidenceCardPointsAtTheUser();
        await CheckAgainAlwaysTries();
        await DoubleTapsRunOnce();
        await LinksThatChangeAreRead();
        await WelcomeRestartsTheGuide();
        await UploadFailuresGetAPlainMessage();
        await CameraPhotoComesAsAStream();
        await DigitsFromAnyAlphabet();
        await LengthCapsAndDates();
        Console.WriteLine("PASS: Page behaviour: double taps, undo, changing story, refused runs, Check again, changing links, uploads and the camera.");
    }

    private static async Task ComposeUndoStopsTheSend()
    {
        await using var app = new App(Draft);
        var page = await app.Host.Mount<Compose>();
        var tapped = await app.Host.DoubleTap(page, "form", null, "onsubmit");
        Assert(app.Host.Text(page).Contains("Sending your message"), "Compose: a double tap starts the countdown");
        await app.Host.Click(page, "Undo");
        await Task.Delay(TimeSpan.FromSeconds(6));
        await tapped;
        Assert(SentTea(app) == 0, "Compose: Undo after a double tap stops the send");
        Assert(!app.Host.Text(page).Contains("Sending your message"), "Compose: Undo goes back to the form");
    }

    private static async Task ComposeSendsOnce()
    {
        await using var app = new App(Draft);
        var page = await app.Host.Mount<Compose>();
        var tapped = await app.Host.DoubleTap(page, "form", null, "onsubmit");
        await tapped;
        Assert(SentTea(app) == 1, "Compose: a double tap sends one message");
    }

    private static async Task ComposeSaysTheRealReason()
    {
        var longSubject = new string('x', 201);
        await using var app = new App($"compose?to=sam%40example.org&subject={longSubject}&body=My%20card%20is%204111%201111%201111%201111");
        var page = await app.Host.Mount<Compose>();
        await app.Host.Submit(page);
        await app.Host.Click(page, "Ask my helper");
        var text = app.Host.Text(page);
        Assert(text.Contains("could not be sent") && !text.Contains("no-reply"), "Compose: a refused hold gives the real reason, not no-reply");
    }

    private static async Task StoryChangeStartsAtItsFirstPage()
    {
        await using var app = new App("stories/x");
        var longStory = await app.Api.MakeStoryAsync(new StoryRequest("A long story", null));
        var shortStory = await app.Api.MakeStoryAsync(new StoryRequest("A short one", null));
        var page = await app.Host.Mount<StoryView>(("Id", longStory.Id));
        for (var i = 0; i < 6; i++) await app.Host.Click(page, "Next page");
        Assert(app.Host.Text(page).Contains("Page 7 of 7"), "Story: at the last page of the long story");

        await app.Host.Set(page, ("Id", shortStory.Id));
        Assert(app.Host.Text(page).Contains("Page 1 of 2"), "Story: a shorter story opens at its first page and does not crash");

        await app.Host.Click(page, "Next page");
        await app.Host.Click(page, "Finish");
        Assert(app.Host.Text(page).Contains("You have read the whole story"), "Story: finished");
        await app.Host.Set(page, ("Id", longStory.Id));
        Assert(app.Host.Text(page).Contains("Page 1 of 7"), "Story: a finished story does not carry over to the next one");

        await app.Host.Click(page, "Next page");
        await app.Host.Set(page, ("Id", longStory.Id));
        Assert(app.Host.Text(page).Contains("Page 2 of 7"), "Story: the same story keeps its place when the page redraws");
    }

    private static async Task DemoPanelSaysWhenARunIsRefused()
    {
        await using var app = new App("demo");
        var page = await app.Host.Mount<DemoPanel>();
        for (var i = 0; i < 12; i++) await app.Host.Click(page, "Simulate incoming email");
        Assert(!app.Host.Text(page).Contains("Too many tries"), "Demo panel: twelve runs a minute are fine");

        await app.Host.Click(page, "Simulate incoming email");
        Assert(app.Host.Text(page).Contains("Too many tries. Please wait a minute."), "Demo panel: a refused scenario says so");

        await app.Host.Change(page, "scenario", "own");
        await app.Host.Change(page, "own-body", "See you at three.");
        await app.Host.Click(page, "Simulate incoming email");
        Assert(app.Host.Text(page).Contains("Too many tries"), "Demo panel: a refused own email says so");
        Assert(app.Host.Field(page, "own-body") == "See you at three.", "Demo panel: a refused own email keeps its words");
    }

    private static async Task EvidenceCardPointsAtTheUser()
    {
        await using var app = new App("helper/x");
        app.Ai.Fail = true;
        var held = (await app.Mail.ReceiveAsync(new InboundMail("Zebra Support Team", "zebra.support@example.org", "Your computer", "Please call us now."), false))!;
        Assert(held.State == MessageState.Held, "Evidence card: the message is held");
        var user = app.Api.Household.UserName;

        var card = await app.Host.Mount<EvidenceCard>(("Message", held));
        var text = app.Host.Text(card);
        Assert(text.Contains($"Call {user} first"), "Evidence card: the button names the user");
        Assert(!text.Contains("Call Zebra"), "Evidence card: the button never names the sender");

        app.Guardian.CallFirst(held.Id);
        await app.Host.Set(card, ("Message", app.Store.Find(held.Id)));
        text = app.Host.Text(card);
        Assert(text.Contains($"You will call {user}") && !text.Contains("You will call Zebra"), "Evidence card: the note names the user");
        Assert(app.Guardian.GetMailActivity().Any(a => a.Text.Contains($"You will call {user}")), "Evidence card: the activity log says the same");
    }

    private static async Task CheckAgainAlwaysTries()
    {
        await using var app = new App("helper");
        app.Ai.Fail = true;
        var held = (await app.Mail.ReceiveAsync(new InboundMail("Priya Sandhu", "priya.sandhu@example.com", "Tea", "See you at three."), false))!;
        Assert(held.AwaitingScan, "Check again: the message waits for the checker");

        await app.Mail.RescanAsync(); // the timer's sweep misses, so this message now backs off
        var before = app.Ai.Calls;
        await app.Mail.RescanAsync();
        Assert(app.Ai.Calls == before, "Check again: the timer's sweep skips a message that is backing off");

        var page = await app.Host.Mount<Queue>();
        await app.Host.Click(page, "Show more");
        await app.Host.Click(page, "Check again");
        Assert(app.Ai.Calls > before, "Check again: the button tries now, whatever the backoff");
        Assert(app.Host.Text(page).Contains("No answer yet"), "Check again: no answer is said out loud");
        Assert(app.Store.Find(held.Id)!.State == MessageState.Held, "Check again: no answer leaves it held");

        app.Ai.Fail = false;
        await app.Host.Click(page, "Check again");
        Assert(app.Host.Text(page).Contains("Done. The checker answered"), "Check again: an answer is said out loud");
    }

    private static async Task DoubleTapsRunOnce()
    {
        await OneCall<Check>("check?example=giftcard", "form", null, "onsubmit");
        await OneCall<StoryNew>("stories/new?example=dentist", "form", null, "onsubmit");
        await OneCall<ShowMe>("show-me?goal=Set%20the%20washer", "form", null, "onsubmit");
        await OneCall<Explain>("explain?example=letter", "form", null, "onsubmit");
        await OneCall<DemoPanel>("demo", "button", "Simulate incoming email", "onclick");
    }

    private static async Task OneCall<T>(string path, string element, string? words, string attribute) where T : IComponent
    {
        await using var app = new App(path);
        var page = await app.Host.Mount<T>();
        app.Ai.Gate = new TaskCompletionSource();
        var both = await app.Host.DoubleTap(page, element, words, attribute);
        var calls = app.Ai.Calls;
        app.Ai.Gate.SetResult();
        await both;
        Assert(calls == 1, $"{typeof(T).Name}: a double tap runs once");
    }

    private static async Task LinksThatChangeAreRead()
    {
        {
            await using var app = new App("calendar/new?title=Hair");
            var page = await app.Host.Mount<CalendarNew>();
            Assert(app.Host.Field(page, "what") == "Hair", "Calendar: the link fills in the form");
            await app.Go("calendar/new?title=Dentist&place=High%20Street");
            Assert(app.Host.Field(page, "what") == "Dentist" && app.Host.Field(page, "place") == "High Street", "Calendar: a new link on the same page is read");
        }
        {
            await using var app = new App("show-me?goal=Make%20the%20bed");
            var page = await app.Host.Mount<ShowMe>();
            Assert(app.Host.Field(page, "goal") == "Make the bed", "Show me: the link fills in the goal");
            await app.Go("show-me?goal=Set%20the%20washer");
            Assert(app.Host.Field(page, "goal") == "Set the washer", "Show me: a new link on the same page is read");
        }
        {
            await using var app = new App("stories/new");
            var page = await app.Host.Mount<StoryNew>();
            await app.Go("stories/new?example=dentist");
            Assert(app.Host.Field(page, "story-when") == "Thursday at 10", "Stories: a new link on the same page is read");
        }
    }

    private static async Task WelcomeRestartsTheGuide()
    {
        await using var app = new App("home");
        await app.Host.Mount<DemoBar>();
        Assert(app.Tour.Active, "Guide: on when the page opens");
        await app.Host.Dispatcher.InvokeAsync(app.Tour.Close);
        await app.Go("home");
        Assert(!app.Tour.Active, "Guide: stays closed when moving around");
        await app.Go("home?tour=1");
        Assert(app.Tour.Active && app.Tour.Index == 0, "Guide: Welcome starts it again in the same session");

        // The layout can change on the way, which makes a new guide bar.
        await app.Host.Dispatcher.InvokeAsync(app.Tour.Close);
        await app.Host.Mount<DemoBar>();
        Assert(app.Tour.Active && app.Tour.Index == 0, "Guide: a new guide bar on a ?tour link starts it too");
    }

    private sealed class BrokenFile(Exception problem) : IBrowserFile
    {
        public string Name => "photo.png";
        public DateTimeOffset LastModified => DateTimeOffset.Now;
        public long Size => 10;
        public string ContentType => "image/png";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => throw problem;
    }

    private static async Task UploadFailuresGetAPlainMessage()
    {
        static async Task Upload(App app, int page, Exception problem)
        {
            var component = app.Host.Component(page);
            var picked = component.GetType().GetMethod("Picked", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await app.Host.Dispatcher.InvokeAsync(() => (Task)picked.Invoke(component, [new InputFileChangeEventArgs([new BrokenFile(problem)])])!);
            await app.Host.Set(page); // a real upload event redraws the page
        }

        await using var app = new App();
        var picker = await app.Host.Mount<PicturePicker>();
        await Upload(app, picker, new InvalidOperationException("The connection went away."));
        Assert(app.Host.Text(picker).Contains("We could not read that file"), "Upload: any failure gets a plain message, and the page lives");
        await Upload(app, picker, new IOException("Too big."));
        Assert(app.Host.Text(picker).Contains("too big"), "Upload: a file that is too big still says so");

        var contact = await app.Host.Mount<ContactEdit>(("Id", app.Mail.GetContacts()[0].Id));
        await Upload(app, contact, new InvalidOperationException("The connection went away."));
        Assert(app.Host.Text(contact).Contains("We could not read that file"), "Contact photo: any failure gets a plain message");
    }

    private sealed class FakePhoto(byte[] bytes, Exception? problem = null) : IJSStreamReference
    {
        public long Length => bytes.Length;
        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (problem is not null) throw problem;
            if (bytes.Length > maxAllowedSize) throw new ArgumentOutOfRangeException(nameof(maxAllowedSize));
            return new ValueTask<Stream>(new MemoryStream(bytes));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }

    private static async Task CameraPhotoComesAsAStream()
    {
        var big = new byte[256 * 1024]; // far past the 32 KB limit on one message
        big[0] = 0xFF;
        Type? asked = null;
        IJSStreamReference photo = new FakePhoto(big);

        await using var app = new App();
        app.Js.Answer = (identifier, type) =>
        {
            if (identifier == "backstopCamera.start") return true;
            if (identifier != "backstopCamera.capture") return null;
            asked = type;
            return photo;
        };
        PicturePicker.PickedFile? got = null;
        var owner = new object();
        var page = await app.Host.Mount<CameraCapture>(("OnPhoto", EventCallback.Factory.Create<PicturePicker.PickedFile>(owner, f => got = f)));

        await app.Host.Click(page, "Use the camera");
        await app.Host.Click(page, "Take the picture");
        Assert(asked == typeof(IJSStreamReference), "Camera: the photo is asked for as a stream, never as one large message");
        Assert(got is not null && got.Bytes.Length == big.Length && got.Bytes[0] == 0xFF, "Camera: the whole photo arrives");

        photo = new FakePhoto(new byte[11 * 1024 * 1024]);
        got = null;
        await app.Host.Click(page, "Take it again");
        await app.Host.Click(page, "Take the picture");
        Assert(got is null && app.Host.Text(page).Contains("We could not take the picture"), "Camera: a photo that is too big gets a plain message");
    }

    private static Task DigitsFromAnyAlphabet()
    {
        var mail = new MockMailService(new MailStore());
        // Arabic-Indic digits, written with escapes so this file stays plain.
        Assert(mail.CheckOutgoing("sam@example.org", "Hi", "١٢-٣٤-٥٦").NeedsPause, "Digits: a bank code in another alphabet does not crash, and is caught");
        Assert(mail.CheckOutgoing("sam@example.org", "Hi", "٤١١١ ١١١١ ١١١١ ١١١١").NeedsPause, "Digits: a card number in another alphabet is caught");
        Assert(mail.CheckOutgoing("sam@example.org", "Hi", "12-34-56").NeedsPause, "Digits: plain digits still work");
        Assert(!mail.CheckOutgoing("sam@example.org", "Hi", "See you on 27-10-26.").NeedsPause, "Digits: a date is still left alone");
        return Task.CompletedTask;
    }

    private static async Task LengthCapsAndDates()
    {
        await using var app = new App();
        var api = app.Api;
        static async Task<bool> Refused(Func<Task> call)
        {
            try { await call(); return false; }
            catch (BackstopApiException) { return true; }
        }

        Assert(await Refused(() => api.MakeStoryAsync(new StoryRequest(new string('x', 1001), null))), "Caps: a very long story topic is refused");
        Assert(await Refused(() => api.MakeStoryAsync(new StoryRequest("The dentist", new string('x', 101)))), "Caps: a very long when is refused");
        Assert(await Refused(() => api.GenerateGuideAsync(new GuideRequest(new string('x', 301), null, null))), "Caps: a very long goal is refused");
        Assert(await Refused(() => api.AskMemoryAsync(new string('x', 501))), "Caps: a very long memory question is refused");
        var seeded = (await api.GetMemoryCardsAsync())[0];
        Assert(await Refused(() => api.RenameMemoryAsync(seeded.Id, new string('x', 81))), "Caps: a very long memory title is refused");
        Assert((await api.GetMemoryCardAsync(seeded.Id))!.Title == seeded.Title, "Caps: a refused title changes nothing");

        var calls = app.Ai.Calls;
        await api.MakeStoryAsync(new StoryRequest(new string('x', 1000), new string('x', 100)));
        Assert(app.Ai.Calls == calls + 1, "Caps: right at the limit is fine");

        var readerDay = new DateOnly(2031, 2, 3);
        var guide = (await api.GenerateGuideAsync(new GuideRequest("Set the washer", null, null)))!;
        Assert((await api.SaveGuideToMemoryAsync(guide.Id, readerDay)).Date == readerDay, "Dates: a saved guide takes the reader's date");
        var letter = await api.ExplainContentAsync(new ExplainRequest("The words.", null, null, ReadingLevel.Simple, ExplainFormat.Summary));
        Assert((await api.SaveExplanationToMemoryAsync(letter.Id, readerDay)).Date == readerDay, "Dates: a saved letter takes the reader's date");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
