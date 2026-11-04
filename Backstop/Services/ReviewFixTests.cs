#if DEBUG
using System.Net;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Accounts;
using Backstop.Services.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backstop.Tests;

/// <summary>
/// Fixes from the 3 Nov code review: a helper's decision cannot be overtaken by a rescan, rescans back off, Meta retries count
/// against the daily cap, shared state stays bounded and private to each visitor where it should be, and sign-in locks are per visitor.
/// Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class ReviewFixTests
{
    // Fails while Fail is true, then says "looks safe". Counts every call.
    private sealed class SwitchMeta : IMetaScamChecker
    {
        public bool Fail = true;
        public int Calls;
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Fail
                ? new MetaScamAnalysis(Verdict.BeCareful, "No answer.", [], false, "down")
                : new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true));
        }
    }

    private static readonly InboundMail Tea = new("Priya Sandhu", "priya.sandhu@example.com", "Tea", "See you at three.");

    public static async Task RunAsync()
    {
        // ---- A rescan cannot release mail a helper has said "Not safe" to, or will call about first ----
        {
            var store = new MailStore();
            var ai = new SwitchMeta();
            var now = DateTimeOffset.Now;
            var mail = new MockMailService(store, ai);
            var guardian = new MockGuardianService(store) { AutoCommit = false, Now = () => now };

            var m = (await mail.ReceiveAsync(Tea, false))!;
            Assert(m.State == MessageState.Held && m.AwaitingScan, "The AI did not answer, so the message is held and waits for a scan");
            ai.Fail = false;
            Assert(guardian.Propose(m.Id, safe: false).Ok, "Not safe can be chosen while it waits for a scan");
            Assert(store.Find(m.Id)!.DecisionPending, "The pending decision is marked on the message");
            Assert(await mail.RescanAsync(m.Id) == 0 && store.Find(m.Id)!.State == MessageState.Held, "A rescan during the undo window changes nothing");
            Assert(await mail.RescanAsync() == 0 || store.Find(m.Id)!.State == MessageState.Held, "The timer's rescan leaves it alone too");
            now = now.AddSeconds(MockGuardianService.DecisionSeconds);
            guardian.CommitDue();
            var after = store.Find(m.Id)!;
            Assert(after.State == MessageState.Blocked && after.HelperDecision == "Blocked" && !after.DecisionPending, "The helper's Not safe is saved when the window ends");

            ai.Fail = true;
            var call = (await mail.ReceiveAsync(Tea with { Subject = "Lunch" }, false))!;
            ai.Fail = false;
            guardian.CallFirst(call.Id);
            Assert(await mail.RescanAsync(call.Id) == 0 && store.Find(call.Id)!.State == MessageState.Held, "Call them first: a rescan does not release it");

            ai.Fail = true;
            var undone = (await mail.ReceiveAsync(Tea with { Subject = "Supper" }, false))!;
            guardian.Propose(undone.Id, safe: false);
            Assert(guardian.Undo(undone.Id) && !store.Find(undone.Id)!.DecisionPending, "Undo takes the pending mark away again");
            ai.Fail = false;
            Assert(await mail.RescanAsync(undone.Id) == 1 && store.Find(undone.Id)!.State == MessageState.Cleared, "After an undo the rescan works as before");
        }

        // ---- A decision that loses the race is reported, never dropped without a word ----
        {
            var store = new MailStore();
            var mail = new MockMailService(store, new SwitchMeta { Fail = false });
            var now = DateTimeOffset.Now;
            var guardian = new MockGuardianService(store) { AutoCommit = false, Now = () => now };
            var m = (await mail.ReceiveAsync("remote-support", false))!;
            guardian.Propose(m.Id, safe: false);
            guardian.Approve(m.Id); // another window approves it first
            now = now.AddSeconds(MockGuardianService.DecisionSeconds);
            guardian.CommitDue();
            Assert(guardian.TakeFailure(m.Id) is { } why && why.Contains("another window"), "The helper is told their choice was not saved");
        }

        // ---- Timer rescans back off after each miss; a helper's Check again always tries ----
        {
            var store = new MailStore();
            var ai = new SwitchMeta();
            var clock = DateTimeOffset.Now;
            var mail = new MockMailService(store, ai) { Clock = () => clock };
            await mail.ReceiveAsync(Tea, false);
            var start = ai.Calls;
            await mail.RescanAsync();
            var first = ai.Calls - start;
            await mail.RescanAsync();
            var second = ai.Calls - start - first;
            Assert(first > 0 && second == 0, "A second sweep straight away does not ask again");
            clock = clock.AddSeconds(50);
            var before = ai.Calls;
            await mail.RescanAsync();
            Assert(ai.Calls - before == first, "After the wait it asks again");
            clock = clock.AddSeconds(50);
            before = ai.Calls;
            await mail.RescanAsync();
            Assert(ai.Calls == before, "The wait doubles after a second miss");
            var id = store.Query(x => x.AwaitingScan).First().Id;
            before = ai.Calls;
            await mail.RescanAsync(id);
            Assert(ai.Calls == before + 1, "Check again ignores the wait");
        }

        // ---- Retries count against the daily cap, and are not sent past it ----
        {
            var hits = 0;
            var guard = new MetaCallGuard(1000, null, 1000, dayLimit: 2);
            var services = new ServiceCollection();
            services.AddSingleton(guard).AddTransient<MetaRetryBudget>();
            var client = services.AddHttpClient("meta").ConfigurePrimaryHttpMessageHandler(() => new Always(HttpStatusCode.InternalServerError, () => hits++));
            client.AddStandardResilienceHandler(o => { o.Retry.MaxRetryAttempts = 2; o.Retry.Delay = TimeSpan.FromMilliseconds(1); o.Retry.UseJitter = false; });
            client.AddHttpMessageHandler<MetaRetryBudget>();
            var http = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("meta");

            Assert(guard.TryEnter(out _, out _), "The caller counts the first attempt, as the checker does");
            using var response = await http.PostAsync("https://api.meta.ai/v1/chat/completions", new StringContent("{}"));
            Assert(hits == 2 && response.StatusCode == HttpStatusCode.TooManyRequests, $"One retry fits the cap and is sent; the next is refused unsent (sent {hits})");
            Assert(!guard.TryEnter(out _, out _), "The day's cap is used up by the retries");
        }

        // ---- The shared store stays bounded, and hands out copies ----
        {
            var store = new MailStore();
            var copy = store.Find("seed1")!;
            copy.Summary = "changed outside the store";
            Assert(store.Find("seed1")!.Summary != copy.Summary, "Changing a copy does not change the store");
            for (var i = 0; i < MailStore.MaxMessages + 50; i++)
                store.Add(new MailMessage { Id = $"x{i}", FromName = "A", FromAddress = "a@example.org", Subject = "s", Body = "b", State = MessageState.Blocked });
            Assert(store.Count(_ => true) <= MailStore.MaxMessages, "Messages are capped");
            Assert(store.Find("seed1") is not null && store.Count(m => m.State == MessageState.Held) == 1, "Blocked mail goes first; inbox and held mail stay");
            for (var i = 0; i < MailStore.MaxSent + 20; i++)
                store.AddSent(new SentMessage { Id = $"s{i}", ToName = "", ToAddress = "friend@example.org", Subject = "s", Body = "b" }, null);
            Assert(store.Sent().Count <= MailStore.MaxSent, "Sent mail is capped");
        }

        // ---- "Make these modes fail" belongs to one visitor; Empty Remember needs the reset code ----
        {
            var options = new MockApiOptions();
            try
            {
                ClientIdentity.Current = "203.0.113.10";
                options.FailRequests = true;
                Assert(options.FailRequests, "The visitor who switched it on sees it on");
                ClientIdentity.Current = "203.0.113.20";
                Assert(!options.FailRequests, "Another visitor is not affected");
                ClientIdentity.Current = "203.0.113.10";
                options.ResetAll();
                Assert(!options.FailRequests, "Reset switches it off for everyone");
            }
            finally { ClientIdentity.Current = null; }

            var store = new MailStore();
            var guardian = new MockGuardianService(store);
            var api = new MockBackstopApi(guardian, options, null);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DEMO_RESET_SECRET"] = "lantern-otter" }).Build();
            var demo = new DemoControl(new MockMailService(store), api, options, config, NullLogger<DemoControl>.Instance);
            var had = (await api.GetMemoryCardsAsync()).Count;
            Assert(had > 0 && !demo.ClearMemories("wrong").Done && (await api.GetMemoryCardsAsync()).Count == had, "Empty Remember without the code changes nothing");
            Assert(demo.ClearMemories("lantern-otter").Done && (await api.GetMemoryCardsAsync()).Count == 0, "With the code it empties Remember");

            // Show me keeps no photo when no guide matches, and photos have a total budget.
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];
            Assert(await api.GenerateGuideAsync(new GuideRequest("xyzzy plugh", png, "a.png")) is null && api.ModePhotoBytes == 0, "No guide: the photo is not kept");
            var big = new ContactPhoto(new byte[9 * 1024 * 1024], "image/png");
            for (var i = 0; i < 12; i++) api.KeepModePhoto($"p{i}", big);
            Assert(api.ModePhotoBytes <= MockBackstopApi.ModePhotoBudgetBytes && api.GetPhoto("p11") is not null && api.GetPhoto("p0") is null, "Oldest photos go first past the budget");
        }

        // ---- A stranger's wrong passwords cannot lock the real person out ----
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ACCOUNT_USER_PASSWORD"] = "quiet-harbour-41" }).Build();
            var accounts = new AccountStore(config);
            for (var i = 0; i < 5; i++) accounts.Verify("sam@example.com", "guess", "198.51.100.7");
            Assert(accounts.Verify("sam@example.com", "quiet-harbour-41", "198.51.100.7") is null, "Five misses lock that visitor out");
            Assert(accounts.Verify("sam@example.com", "quiet-harbour-41", "198.51.100.8") is not null, "Someone else can still sign in");
        }

        Console.WriteLine("PASS: Review fixes: decisions beat rescans, rescans back off, retries are capped, shared state is bounded and per visitor, sign-in locks are per visitor.");
    }

    private sealed class Always(HttpStatusCode status, Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}

#endif
