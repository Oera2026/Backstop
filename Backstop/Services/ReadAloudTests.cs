#if DEBUG
#pragma warning disable BL0006
using Backstop.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Backstop.Tests;

/// <summary>
/// Read aloud button tests. The project has no bUnit, so this uses Blazor's own Renderer with a fake browser:
/// the real component is rendered, clicked and disposed. Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class ReadAloudTests
{
    private sealed class FakeJs(bool supported) : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public string? Spoken { get; private set; }
        public DotNetObjectReference<ReadAloud>? Ref { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            object? result = null;
            if (identifier == "backstop.canSpeak") result = supported;
            else if (identifier == "backstop.speakWithEnd") { Ref = (DotNetObjectReference<ReadAloud>)args![0]!; Spoken = (string?)args[1]; result = true; }
            return new ValueTask<TValue>((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Host(IServiceProvider sp) : Renderer(sp, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new Exception("Render failed", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        public async Task<(ReadAloud Component, int Id)> Mount(string? text) => await Dispatcher.InvokeAsync(async () =>
        {
            var c = (ReadAloud)InstantiateComponent(typeof(ReadAloud));
            var id = AssignRootComponentId(c);
            await RenderRootComponentAsync(id, With(text));
            return (c, id);
        });

        public Task SetText(int id, string? text) => Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id, With(text)));
        public Task Remove() => Dispatcher.InvokeAsync(async () => await DisposeAsync());

        private static ParameterView With(string? text) => ParameterView.FromDictionary(new Dictionary<string, object?> { ["Text"] = text });

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

    // What the button looks like right now. A real button element is keyboard focusable, so Focusable checks type="button".
    private sealed record Look(string? Pressed, string? Label, string Words, bool Focusable);

    private static Look? Button(List<RenderTreeFrame> tree)
    {
        var i = tree.FindIndex(f => f.FrameType == RenderTreeFrameType.Element && f.ElementName == "button");
        if (i < 0) return null;
        string? pressed = null, label = null, type = null;
        var j = i + 1;
        for (; j < tree.Count && tree[j].FrameType == RenderTreeFrameType.Attribute; j++)
        {
            if (tree[j].AttributeName == "aria-pressed") pressed = tree[j].AttributeValue?.ToString();
            if (tree[j].AttributeName == "aria-label") label = tree[j].AttributeValue?.ToString();
            if (tree[j].AttributeName == "type") type = tree[j].AttributeValue?.ToString();
        }
        var words = string.Concat(tree.Skip(j).Where(f => f.FrameType == RenderTreeFrameType.Text).Select(f => f.TextContent)).Trim();
        return new Look(pressed, label, words, type == "button");
    }

    private static async Task<(Host Host, FakeJs Js, int Id)> Start(string? text, bool supported = true)
    {
        var js = new FakeJs(supported);
        var host = new Host(new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider());
        var (_, id) = await host.Mount(text);
        await Task.Delay(30); // the support check runs after the first render
        return (host, js, id);
    }

    public static async Task RunAsync()
    {
        // Renders when there is an explanation, and says what it does.
        var (host, js, id) = await Start("This looks like a scam. Please do not reply.");
        var b = Button(host.Tree(id));
        Assert(b is { Pressed: "false", Label: "Read aloud", Words: "Read aloud", Focusable: true }, "The button shows Read aloud, not pressed");

        // Hidden when there is nothing to read.
        foreach (var empty in new string?[] { null, "", "   " })
        {
            var (h, _, i) = await Start(empty);
            Assert(Button(h.Tree(i)) is null, "No explanation: no button");
        }

        // Hidden, not an error, when the browser cannot speak.
        var (hu, ju, iu) = await Start("Some words.", supported: false);
        Assert(Button(hu.Tree(iu)) is null && !ju.Calls.Contains("backstop.speakWithEnd"), "No speech support: no button, no error");

        // Press: it speaks exactly the text and turns into Stop. Press again: it stops.
        await host.Click(id);
        b = Button(host.Tree(id));
        Assert(js.Spoken == "This looks like a scam. Please do not reply." && b is { Pressed: "true", Label: "Stop reading aloud", Words: "Stop" }, "Pressed: speaking, shows Stop");
        await host.Click(id);
        b = Button(host.Tree(id));
        Assert(js.Calls.Contains("backstop.stop") && b is { Pressed: "false", Words: "Read aloud" }, "Pressed again: stopped, shows Read aloud");

        // The voice finishing by itself puts the button back.
        await host.Click(id);
        await host.Dispatcher.InvokeAsync(() => js.Ref!.Value.Ended());
        await Task.Delay(30);
        Assert(Button(host.Tree(id)) is { Pressed: "false", Words: "Read aloud" }, "Speech ended: back to Read aloud");

        // A new result replaces the text while speaking: the old voice stops.
        await host.Click(id);
        js.Calls.Clear();
        await host.SetText(id, "A different explanation.");
        Assert(js.Calls.Contains("backstop.stop") && Button(host.Tree(id)) is { Pressed: "false" }, "New result: speech stops");

        // Leaving the page stops it.
        await host.Click(id);
        js.Calls.Clear();
        await host.Remove();
        Assert(js.Calls.Contains("backstop.stop"), "Navigating away stops the voice");

        Console.WriteLine("PASS: Read aloud button renders with an explanation, hides without one or without speech, toggles and stops.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}

#endif
