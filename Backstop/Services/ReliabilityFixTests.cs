#if DEBUG
using System.Net;
using System.Text;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backstop.Tests;

/// <summary>
/// Fixes from the 4 Nov audit: Meta time limits and retries, caps on shared state, resets, tiers, the rules on Send and on held mail.
/// Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class ReliabilityFixTests
{
    private const string Fake = "test-fake-key";

    private sealed class Scripted(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            answer(++Calls, request, ct);
    }

    private static HttpResponseMessage Say(string content, string finish = "stop")
    {
        var outer = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { content }, finish_reason = finish } } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(outer, Encoding.UTF8, "application/json") };
    }

    private const string SafeJson = "{\"verdict\":\"LooksSafe\",\"reason\":\"This looks fine.\",\"needsHelper\":false,\"personAtRisk\":false,\"flagIds\":[]}";

    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var all = new Dictionary<string, string?> { ["META_AI_KEY"] = Fake };
        foreach (var (k, v) in values) all[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(all).Build();
    }

    private static MetaAiScamChecker Checker(HttpMessageHandler handler, params (string, string?)[] values) =>
        new(new HttpClient(handler), Config(values), NullLogger<MetaAiScamChecker>.Instance);

    // The real clients as the app builds them, with a stand-in for the network.
    private static ServiceProvider RealClients(HttpMessageHandler network, IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        MetaClients.Add(services, config);
        services.AddHttpClient<MetaAiScamChecker>().ConfigurePrimaryHttpMessageHandler(() => network);
        return services.BuildServiceProvider();
    }

    private static readonly InboundMail Tea = new("Priya Sandhu", "priya.sandhu@example.com", "Tea", "See you at three.");

    public static async Task RunAsync()
    {
        await TimeLimits();
        await UnreadableReplies();
        Options();
        await Rescans();
        await Caps();
        await Resets();
        await Listeners();
        await Tiers();
        await Store();
        await Send();
        await Propose();
        Console.WriteLine("PASS: Reliability: Meta limits and retries, caps, resets, tiers, Send and held mail.");
    }

    private static async Task TimeLimits()
    {
        // Every client builds, including a raised timeout, and none is held to HttpClient's 100 second default.
        foreach (var seconds in new[] { "45", "200", "300" })
        {
            await using var sp = RealClients(new Scripted((_, _, _) => Task.FromResult(Say(SafeJson))), Config(("META_AI_TIMEOUT_SECONDS", seconds)));
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            foreach (var name in new[] { nameof(MetaAiScamChecker), nameof(MetaAssistant), nameof(MetaStoryWriter), nameof(MetaDocumentExplainer), nameof(MetaGuideWriter), nameof(MetaMemoryAnswerer) })
                Assert(factory.CreateClient(name).Timeout == Timeout.InfiniteTimeSpan, $"{name} with {seconds}s: the handler owns the time limits");
            Assert((await sp.GetRequiredService<MetaAiScamChecker>().CheckScamAsync("Hello")).Success, $"With {seconds}s the pipeline's settings are valid");
        }

        var options = MetaAiOptions.Load(Config(("META_AI_TIMEOUT_SECONDS", "200")));
        Assert(options.AttemptTimeout == TimeSpan.FromSeconds(200), "A timeout above 100 means what it says");
        Assert(options.CallBudget > options.AttemptTimeout * 2, "The whole call leaves room for two tries");

        // A slow reply really gets its second try, and the second answer is used.
        var slowFirst = new Scripted(async (n, _, ct) =>
        {
            if (n == 1) await Task.Delay(Timeout.Infinite, ct);
            return Say(SafeJson);
        });
        await using (var sp = RealClients(slowFirst, Config(("MetaAI:TimeoutSeconds", "2"))))
        {
            var r = await sp.GetRequiredService<MetaAiScamChecker>().CheckScamAsync("Hello");
            Assert(r.Success && slowFirst.Calls == 2, "A try that runs out of time is tried once more");
        }

        // Both slow: held, never safe, inside the budget.
        var alwaysSlow = new Scripted(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return Say(SafeJson); });
        await using (var sp = RealClients(alwaysSlow, Config(("MetaAI:TimeoutSeconds", "2"))))
        {
            var r = await sp.GetRequiredService<MetaAiScamChecker>().CheckScamAsync("Hello");
            Assert(!r.Success && r.Verdict != Verdict.LooksSafe && alwaysSlow.Calls == 2, "Two slow tries fail closed");
        }
    }

    private static async Task UnreadableReplies()
    {
        // Empty or cut off: one more try, and the second answer counts.
        foreach (var bad in new[] { "", "{\"verdict\":\"LooksSa" })
        {
            var net = new Scripted((n, _, _) => Task.FromResult(n == 1 ? Say(bad, "length") : Say(SafeJson)));
            Assert((await Checker(net).CheckScamAsync("Hello")).Success && net.Calls == 2, "An empty or cut-off reply is tried once more");
        }

        // Never more than one more, and still held.
        var never = new Scripted((_, _, _) => Task.FromResult(Say("", "length")));
        var held = await Checker(never).CheckScamAsync("Hello");
        Assert(!held.Success && held.Verdict != Verdict.LooksSafe && never.Calls == 2, "Two unreadable replies fail closed after two calls");

        // An answer that was read is not asked for again, and neither is a refusal from Meta.
        var fine = new Scripted((_, _, _) => Task.FromResult(Say(SafeJson)));
        await Checker(fine).CheckScamAsync("Hello");
        Assert(fine.Calls == 1, "A good reply is one call");
        var denied = new Scripted((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Checker(denied).CheckScamAsync("Hello");
        Assert(denied.Calls == 1, "A refusal is not a reason to read again");

        // Each try is counted against the caps.
        var guard = new MetaCallGuard(limit: 100);
        var counted = new Scripted((_, _, _) => Task.FromResult(Say("")));
        await new MetaAiScamChecker(new HttpClient(counted), Config(), NullLogger<MetaAiScamChecker>.Instance, guard).CheckScamAsync("Hello");
        guard.TryEnter(out _, out var used);
        Assert(used == 3, "Both tries count against the cap");

        // The token cap leaves room for the reasoning Muse does first.
        string? sent = null;
        var capture = new Scripted(async (_, req, _) => { sent = await req.Content!.ReadAsStringAsync(); return Say(SafeJson); });
        await Checker(capture).CheckScamAsync("Hello");
        Assert(sent!.Contains("\"max_tokens\":4000"), "The scam check has the same token room as Ask Me");

        // Words around the JSON no longer cause a false hold. Two objects, or no verdict, still fail closed.
        var chatty = new Scripted((_, _, _) => Task.FromResult(Say("Here is my answer:\n```json\n" + SafeJson + "\n```\nI hope that helps.")));
        Assert((await Checker(chatty).CheckScamAsync("Hello")).Success, "Words around the JSON are ignored");
        var two = new Scripted((_, _, _) => Task.FromResult(Say(SafeJson + " {\"verdict\":\"DontReply\"}")));
        Assert(!(await Checker(two).CheckScamAsync("Hello")).Success, "Two answers in one reply fail closed");

        // Ask Me: a helper flag survives when the second try also fails.
        var assistantNet = new Scripted((n, _, _) => Task.FromResult(n == 1
            ? Say("{\"needsHelper\":true,\"personAtRisk\":true,\"suggest\":\"none\"}")
            : Say("")));
        var reply = await new MetaAssistant(new HttpClient(assistantNet), Config(), NullLogger<MetaAssistant>.Instance).ReplyAsync([], "Hello");
        Assert(!reply.Success && reply.AtRisk && reply.NeedsHuman && assistantNet.Calls == 2, "A flag from either try still brings the helper in");
        var laterFlag = new Scripted((n, _, _) => Task.FromResult(n == 1
            ? Say("")
            : Say("{\"reply\":\"I have told your helper.\",\"needsHelper\":\"true\",\"personAtRisk\":false,\"suggest\":\"none\"}")));
        var flagged = await new MetaAssistant(new HttpClient(laterFlag), Config(), NullLogger<MetaAssistant>.Instance).ReplyAsync([], "Hello");
        Assert(flagged.Success && flagged.NeedsHuman, "The word true counts in Ask Me too");
    }

    private static void Options()
    {
        var odd = Config(("MetaAI:TimeoutSeconds", "soon"), ("META_AI_TIMEOUT_SECONDS", "60"), ("MetaAI:MaxCallsPerMinute", "lots"));
        Assert(MetaAiOptions.Load(odd).TimeoutSeconds == 60, "A mistyped nested timeout is ignored and the app starts");
        Assert(new MetaCallGuard(odd).Limit == 40, "A mistyped call limit falls back to the default");
        Assert(MetaAiOptions.Load(Config(("MetaAI:Model", "muse-wonder-1.0"), ("MetaAI:EndpointUrl", "https://api.meta.ai/v2"), ("MetaAI:ApiKey", "other"))) is { Model: "muse-wonder-1.0", EndpointUrl: "https://api.meta.ai/v2", ApiKey: "other" }, "The nested settings are still read");
        Assert(MetaAiOptions.Load(Config(("MetaAI:TimeoutSeconds", "999999"))).AttemptTimeout == TimeSpan.FromSeconds(300), "A huge timeout is capped, so the handler's settings stay valid");
    }

    private sealed class FailingMeta : IMetaScamChecker
    {
        public int Calls;
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new MetaScamAnalysis(Verdict.BeCareful, "No answer.", [], false, "down"));
        }
    }

    private static async Task Rescans()
    {
        var clock = DateTime.UtcNow;
        var store = new MailStore();
        var ai = new FailingMeta();
        var mail = new MockMailService(store, ai) { RescanGuard = new MetaCallGuard(limit: 3, () => clock, dayLimit: 5) };
        var ids = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var m = MockData.CreateMessage(Tea);
            m.State = MessageState.Held;
            m.AwaitingScan = true;
            store.Add(m);
            ids.Add(m.Id);
        }

        var before = ai.Calls;
        await mail.RescanAsync();
        Assert(ai.Calls - before == 3, "A sweep stops at its own share of calls");
        Assert(ids.All(id => store.Find(id)!.State == MessageState.Held), "What is left waiting stays held");

        // The next minute it carries on, up to the day's share.
        clock = clock.AddMinutes(1);
        await mail.RescanAsync();
        clock = clock.AddMinutes(1);
        await mail.RescanAsync();
        Assert(ai.Calls - before == 5, "A sweep cannot pass its share of the day");

        // A helper asking for one message is not held to the timer's share.
        before = ai.Calls;
        await mail.RescanAsync(ids[0]);
        Assert(ai.Calls - before == 1, "Check again on one message still tries");
    }

    private sealed class GuideStub : IGuideWriter
    {
        public Task<GuideDraft> WriteAsync(string goal, byte[]? photo, string? mimeType, CancellationToken ct = default) =>
            Task.FromResult(new GuideDraft(true, false, "Setting the washer", ["Load it.", "Press Start."], null, "washer", ["home"], "set the washer"));
    }

    private sealed class StoryStub(TaskCompletionSource? gate = null) : IStoryWriter
    {
        public async Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default)
        {
            if (gate is not null) await gate.Task;
            return new StoryDraft(true, "My story", [new StoryPage("Start", "It begins.", "bus")], false);
        }
    }

    private sealed class WaitingMeta(TaskCompletionSource gate) : IMetaScamChecker
    {
        public async Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            await gate.Task;
            return new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true);
        }
    }

    private static async Task Caps()
    {
        var api = new MockBackstopApi(new MockGuardianService(new MailStore()), new MockApiOptions(), new SafeMeta(), null, null, null, new StoryStub(), new GuideStub());
        var seededCards = (await api.GetMemoryCardsAsync()).Count;

        // Finished checks go first, then held ones. An open safeguarding alert is never dropped.
        var alert = await new Func<Task<CheckResult>>(() => api.CheckContentAsync(new CheckRequest(CheckKind.Message, "I am scared of him.", null)))();
        Assert(alert.Status == ReviewStatus.Held && alert.Flags.Any(f => f.Id == "at-risk"), "The alert is open");
        var first = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "See you at three.", null));
        var heldOne = await api.AskTrustedPersonAsync(first.Id);
        Assert(heldOne.Status == ReviewStatus.Held, "A held check to compare with");
        CheckResult? oldFinished = null;
        for (var i = 0; i < MockBackstopApi.MaxStoredChecks + 20; i++)
        {
            var c = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, $"See you at three, number {i}.", null));
            oldFinished ??= c;
        }
        Assert(await api.GetCheckAsync(alert.Id) is not null && api.GetHelperDigest().Items.Any(i => i.Id == alert.Id), "An open safeguarding alert stays past the cap");
        Assert(await api.GetCheckAsync(first.Id) is not null, "A held check stays while finished ones can go");
        Assert(await api.GetCheckAsync(oldFinished!.Id) is null, "The oldest finished check goes first");

        // Everything else stays at its cap, and the newest item is always there.
        Guide? lastGuide = null;
        for (var i = 0; i < MockBackstopApi.MaxStoredGuides + 10; i++)
            lastGuide = await api.GenerateGuideAsync(new GuideRequest("set the washer", null, null));
        Assert(await api.GetGuideAsync(lastGuide!.Id) is not null, "The newest guide is kept");

        Story? firstStory = null, lastStory = null;
        for (var i = 0; i < MockBackstopApi.MaxStoredStories + 10; i++)
        {
            lastStory = await api.MakeStoryAsync(new StoryRequest("Going to the dentist", null));
            firstStory ??= lastStory;
        }
        Assert((await api.GetStoriesAsync()).Count == MockBackstopApi.MaxStoredStories, "Stories are capped");
        Assert(await api.GetStoryAsync(firstStory!.Id) is null && await api.GetStoryAsync(lastStory!.Id) is not null, "The oldest story goes first");

        // Each save needs its own guide, because saving one twice returns the same card.
        for (var i = 0; i < MockBackstopApi.MaxStoredCards + 10; i++)
        {
            var g = await api.GenerateGuideAsync(new GuideRequest("set the washer", null, null));
            await api.SaveGuideToMemoryAsync(g!.Id);
        }
        var cards = await api.GetMemoryCardsAsync();
        Assert(cards.Count == seededCards + MockBackstopApi.MaxStoredCards, "Saved cards are capped and seeded ones stay");
        Assert(cards.Any(c => c.Id == "m1"), "A seeded card is never dropped");

        var log = await api.GetActivityAsync();
        Assert(log.Count > 0, "The activity log still works");

        // Chat stays bounded, and the seeded messages stay.
        for (var i = 0; i < MockBackstopApi.MaxChatMessages + 20; i++)
            await api.SendChatAsHelperAsync($"Message {i}");
        var chat = await api.GetChatLogAsync(Backstop.Services.Api.Fixtures.ChatFixtures.TrustedId);
        Assert(chat.Count <= MockBackstopApi.MaxChatMessages, "Chat is capped");
        Assert(chat.Any(m => m.Text.Contains("ring you at six")), "Seeded chat stays");
    }

    private sealed class SafeMeta : IMetaScamChecker
    {
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default) =>
            Task.FromResult(new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true));
    }

    private static async Task Resets()
    {
        // Work waiting on the AI when the demo resets does not write into the fresh household.
        var gate = new TaskCompletionSource();
        var api = new MockBackstopApi(new MockGuardianService(new MailStore()), new MockApiOptions(), new WaitingMeta(gate), null, null, null, new StoryStub(gate));
        var check = api.CheckContentAsync(new CheckRequest(CheckKind.Message, "A real letter from before the reset.", null));
        var story = api.MakeStoryAsync(new StoryRequest("Going to the dentist", null));
        api.ResetDemo();
        gate.SetResult();
        var c = await check;
        var s = await story;
        Assert(await api.GetCheckAsync(c.Id) is null, "A check that finishes after a reset is not stored");
        Assert(await api.GetStoryAsync(s.Id) is null && (await api.GetStoriesAsync()).Count == 0, "A story that finishes after a reset is not stored");

        // The modes-fail toggle reaches every mode.
        var options = new MockApiOptions();
        var failing = new MockBackstopApi(new MockGuardianService(new MailStore()), options, null, null, null, null, new StoryStub());
        options.FailRequests = true;
        async Task<bool> Fails(Func<Task> call)
        {
            try { await call(); return false; }
            catch (BackstopApiException) { return true; }
        }
        Assert(await Fails(() => failing.MakeStoryAsync(new StoryRequest("Going to the dentist", null))), "Stories fail with the toggle");
        Assert(await Fails(() => failing.ExplainContentAsync(new ExplainRequest("Words.", null, null, ReadingLevel.Simple, ExplainFormat.Summary))), "Explain fails with the toggle");
        Assert(await Fails(() => failing.AskAboutAsync("x", "What does it say?")), "Questions fail with the toggle");
        options.ResetAll();
    }

    private static async Task Listeners()
    {
        // One broken page does not stop the others hearing, or undo the change.
        var store = new MailStore();
        var api = new MockBackstopApi(new MockGuardianService(store), new MockApiOptions());
        var heard = 0;
        api.Changed += () => throw new InvalidOperationException("a closed page");
        api.Changed += () => heard++;
        api.SetPresence(ProfileRole.User, Presence.Hidden);
        Assert(heard == 1 && api.GetProfile(ProfileRole.User).Presence == Presence.Hidden, "A broken listener does not stop the others");

        // Events are not raised while holding the lock: a listener can read straight back from another thread.
        var quiet = new MockBackstopApi(new MockGuardianService(store), new MockApiOptions());
        var readBack = true;
        store.Changed += () => readBack &= Task.Run(() => quiet.ChatPeople).Wait(TimeSpan.FromSeconds(5));
        var held = store.Query(m => m.State == MessageState.Held).First();
        await quiet.ResolveReviewItemAsync(held.Id, ReviewDecision.CallFirst);
        quiet.ResetDemo();
        Assert(readBack, "A decision or a reset does not hold the lock while listeners run");
    }

    private static async Task Tiers()
    {
        // Standard: the helper reads a check and its history.
        var guardian = new MockGuardianService(new MailStore());
        var api = new MockBackstopApi(guardian, new MockApiOptions(), new SafeMeta());
        var check = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "See you at three.", null));
        await api.AskTrustedPersonAsync(check.Id);
        Assert(await api.GetCheckForHelperAsync(check.Id) is not null, "Standard: the helper can read a check they were asked about");
        await api.ResolveReviewItemAsync(check.Id, ReviewDecision.Block);
        Assert(api.GetDecisionHistory().Any(e => e.Title.Contains("checked")), "Standard: the decision is in the history");
        guardian.Block(guardian.GetQueue()[0].Id);
        Assert(guardian.GetMailActivity().Count > 0, "Standard: mail activity can be read");

        // Minimal: none of that, but the user's own view and the safeguarding floor stay.
        var other = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "See you at four.", null));
        await api.AskTrustedPersonAsync(other.Id);
        var alert = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "I am scared of him.", null));
        api.SetHelperAccess(new HelperPermissions(HelperTier.Minimal));
        Assert(await api.GetCheckForHelperAsync(other.Id) is null, "Minimal: a check request cannot be read");
        Assert(await api.GetCheckAsync(other.Id) is not null, "Minimal: the user can still read their own check");
        Assert(await api.GetCheckForHelperAsync(alert.Id) is not null, "Minimal: a safeguarding alert is always readable");
        Assert(!api.GetDecisionHistory().Any(e => e.Title.Contains("checked")), "Minimal: check decisions leave the history");
        Assert(guardian.GetMailActivity().Count == 0, "Minimal: mail activity cannot be read");

        // Helper previews: a link in a subject, a sender name or a new address is defanged.
        const string Linky = "Open https://zebra.example.net/pay now";
        var store = new MailStore();
        var mail = new MockMailService(store);
        var plain = new MockGuardianService(store);
        var withPreviews = new MockBackstopApi(plain, new MockApiOptions());
        var m = (await mail.ReceiveAsync(new InboundMail("See https://zebra.example.net", "zebra@example.org", Linky, "Hello."), false))!;
        var row = withPreviews.GetHelperDigest().Items.First(i => i.Id == m.Id);
        Assert(!row.Detail.Contains("https://") && !row.Title.Contains("https://") && row.Detail.Contains("zebra[.]example[.]net"), "Queue row: subject and sender are defanged");
        Assert(!(await withPreviews.GetReviewItemAsync(m.Id))!.Title.Contains("https://"), "Review item: the subject is defanged");
        var held = mail.HoldOutgoing("friend@example.org", Linky, "Body", ["private"]);
        Assert(held is null && withPreviews.GetHelperDigest().Items.All(i => !i.Detail.Contains("https://")), "Held outgoing: the subject is defanged");
        mail.Send("newperson@example.org", Linky, "Body");
        Assert(withPreviews.GetHelperDigest().Items.Where(i => i.Kind == HelperItemKind.NewPerson).All(i => !i.Preview.Contains("https://") && i.Preview.Contains("zebra[.]example[.]net")), "New person: the subject is defanged");

        // The inbox counts held mail. The helper's badge counts everything that needs them, so a fresh household differs by the seeded help request.
        var freshStore = new MailStore();
        var counts = new MockBackstopApi(new MockGuardianService(freshStore), new MockApiOptions()).GetHelperDigest();
        Assert(counts.WaitingCount == new MockMailService(freshStore).HeldCount + 1 && counts.Items.Any(i => i.Id == "h-seed"), "The helper's count is held mail plus the other things waiting for them");

        // A held chat from someone taken off the list no longer counts for the user.
        var chatApi = new MockBackstopApi(new MockGuardianService(new MailStore()), new MockApiOptions());
        var arthur = chatApi.ChatPeople.First(p => p.Id == "p-arthur");
        await chatApi.SendChatAsync(arthur.Id, "Hello");
        await Task.Delay(2800); // the scripted reply; with no checker it is held
        Assert(await chatApi.GetHeldChatCountAsync() >= 1, "No checker: the reply is held");
        chatApi.RemoveChatPerson(arthur.Id);
        Assert(await chatApi.GetHeldChatCountAsync() == 0, "Taken off the list: the user's count no longer waits for it");
    }

    private static Task Store()
    {
        // Held mail is never dropped to make room. Finished mail goes first, and then new mail is refused.
        var store = new MailStore();
        var held = store.Count(m => m.State == MessageState.Held);
        var mine = new List<string>();
        for (var i = 0; store.Count(_ => true) < MailStore.MaxMessages; i++)
        {
            var id = $"h{i}";
            mine.Add(id);
            Assert(store.Add(new MailMessage { Id = id, FromName = "A", FromAddress = "a@example.org", Subject = "s", Body = "b", State = MessageState.Held }), "There is room");
        }
        Assert(store.Add(new MailMessage { Id = "over", FromName = "A", FromAddress = "a@example.org", Subject = "s", Body = "b", State = MessageState.Held }), "Finished mail makes room");
        Assert(store.Count(m => m.State == MessageState.Held) == held + mine.Count + 1, "Nothing held was dropped");

        while (store.Count(m => m.State is MessageState.Cleared or MessageState.Blocked) > 0)
            Assert(store.Add(new MailMessage { Id = Guid.NewGuid().ToString("N"), FromName = "A", FromAddress = "a@example.org", Subject = "s", Body = "b", State = MessageState.Held }), "Finished mail still makes room");

        var full = store.Count(_ => true);
        Assert(!store.Add(new MailMessage { Id = "refused", FromName = "A", FromAddress = "a@example.org", Subject = "s", Body = "b", State = MessageState.Held }), "All held: new mail is refused");
        Assert(store.Count(_ => true) == full && store.Find("refused") is null && mine.All(id => store.Find(id) is not null), "Everything held is still there");
        return Task.CompletedTask;
    }

    private static async Task Send()
    {
        var store = new MailStore();
        var mail = new MockMailService(store);
        var sentBefore = store.Sent().Count;

        // The outgoing check runs inside Send, whichever page calls it.
        var r = mail.Send("friend@example.org", "My card", "My card number is 4242 4242 4242 4242.");
        Assert(r.Refused is { Length: > 0 } && r.Message is null && store.Sent().Count == sentBefore, "Private details are refused by Send itself");
        Assert(mail.Send("friend@example.org", "Tea", "See you at three.").Refused is null, "An ordinary message still goes");

        // HoldOutgoing says the real reason.
        Assert(mail.HoldOutgoing("not an address", "Hi", "x", [])!.Contains("email address"), "A bad address says so");
        Assert(mail.HoldOutgoing("friend@example.org", new string('s', 300), "x", [])!.Contains("subject"), "A long subject says so");
        Assert(mail.HoldOutgoing("friend@example.org", "Hi", new string('x', 20001), [])!.Contains("too long"), "A long message says so");
        Assert(mail.HoldOutgoing("no-reply@example.org", "Hi", "x", [])!.Contains("no-reply"), "A no-reply address says so");
        Assert(mail.HoldOutgoing("friend@example.org", "Hi", "x", ["private"]) is null && store.OpenHeldOutgoing().Count == 1, "A good one is kept for the helper");

        // /try: a harmless shortener passes here, but it does not promise what real mail would not do.
        var report = new LinkReport("hxxps://bit[.]ly/x", "hxxps://shop[.]example[.]net/", [], [], LinkVerdict.NoWarnings, Verified: true, Fetched: true, "No warning signs found.");
        var tryMail = new MockMailService(new MailStore(), new SafeMeta(), new InspectorStub(report));
        var tried = await tryMail.TryCheckAsync(new InboundMail("Priya Sandhu", "priya.sandhu@example.com", "Photos", "Here they are https://bit.ly/x"), false);
        Assert(tried.Verdict == TryVerdict.Safe && tried.HelperSees.Contains("real mail"), "A shortener on /try says real mail would wait for the helper");
        var noLink = await tryMail.TryCheckAsync(Tea, false);
        Assert(noLink.Verdict == TryVerdict.Safe && noLink.HelperSees.Contains("straight to your inbox"), "With no link the promise stands");
    }

    private sealed class InspectorStub(LinkReport report) : Backstop.Services.Links.ILinkInspector
    {
        public Task<LinkReport> InspectAsync(string url, string? shownText = null, CancellationToken ct = default) => Task.FromResult(report);
    }

    private static async Task Propose()
    {
        // One tab's Undo cannot cancel a choice made in another: the second tab's choice was refused.
        var store = new MailStore();
        var now = DateTimeOffset.Now;
        var guardian = new MockGuardianService(store) { AutoCommit = false, Now = () => now };
        var m = (await new MockMailService(store).ReceiveAsync(Tea, false))!;
        Assert(guardian.Propose(m.Id, false).Ok, "First tab: Not safe");
        Assert(!guardian.Propose(m.Id, false).Ok, "Second tab: refused, even for the same choice");
        Assert(guardian.GetPending(m.Id) is { Safe: false }, "The first tab's choice still stands");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
