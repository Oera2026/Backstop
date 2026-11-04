#if DEBUG
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;

namespace Backstop.Tests;

/// <summary>
/// What a visitor adds goes to Meta AI and is then disposed of: their own email on /demo is never stored, and everything
/// from Check, Explain, Show me, Stories and Ask Me is deleted after its time. Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class VisitorDataTests
{
    private sealed class SafeMeta(bool ok) : IMetaScamChecker
    {
        public int Calls;
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(ok
                ? new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true)
                : new MetaScamAnalysis(Verdict.BeCareful, "No answer.", [], false, "down"));
        }
    }

    private sealed class GuideStub : IGuideWriter
    {
        public byte[]? Photo;
        public Task<GuideDraft> WriteAsync(string goal, byte[]? photo, string? mimeType, CancellationToken ct = default)
        {
            Photo = photo;
            return Task.FromResult(new GuideDraft(true, false, "Setting the washer", ["Load it.", "Press Start."], null, "washer", ["home"], "set the washer"));
        }
    }

    private sealed class ExplainStub : IDocumentExplainer
    {
        public Task<DocumentReading> ExplainAsync(string? text, byte[]? image, string? mimeType, ReadingLevel level, ExplainFormat format, CancellationToken ct = default) =>
            Task.FromResult(new DocumentReading(true, "letter", "A letter", "Overview.", null, [], [], [], "The words of the letter.", false));
        public Task<DocumentAnswer> AnswerAsync(string documentText, string question, CancellationToken ct = default) =>
            Task.FromResult(new DocumentAnswer(true, "Yes.", false));
    }

    private sealed class StoryStub : IStoryWriter
    {
        public Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default) =>
            Task.FromResult(new StoryDraft(true, "My story", [new StoryPage("Start", "It begins.", "bus")], false));
    }

    private static readonly InboundMail Tea = new("Priya Sandhu", "priya.sandhu@example.com", "Tea", "See you at three.");

    public static async Task RunAsync()
    {
        // ---- The visitor's own email: checked, never stored ----
        {
            var store = new MailStore();
            var total = store.Count(_ => true);
            var held = store.Count(m => m.State == MessageState.Held);

            var ai = new SafeMeta(true);
            var mail = new MockMailService(store, ai);
            Assert(await mail.CheckOnceAsync(Tea, false) == MessageState.Cleared && ai.Calls == 1, "A clean email goes to the AI and comes back Cleared");
            Assert(store.Count(_ => true) == total && mail.GetInbox().All(m => m.Subject != "Tea"), "A Cleared one is not in the store or the inbox");

            var down = new SafeMeta(false);
            var downMail = new MockMailService(store, down);
            Assert(await downMail.CheckOnceAsync(Tea, false) == MessageState.Held, "The AI not answering is Held");
            Assert(await new MockMailService(store).CheckOnceAsync(Tea, false) == MessageState.Held, "No checker is Held");
            Assert(await mail.CheckOnceAsync(Tea, true) == MessageState.Held, "A crash is Held");
            Assert(store.Count(_ => true) == total && store.Count(m => m.State == MessageState.Held) == held, "A held one joins no queue");

            var calls = down.Calls;
            await downMail.RescanAsync();
            Assert(down.Calls == calls + store.Count(m => m.State == MessageState.Held && m.AwaitingScan), "Rescans never see it");

            var capped = new MockMailService(store, new SafeMeta(true));
            for (var i = 0; i < 12; i++) await capped.CheckOnceAsync(Tea, false);
            Assert(await capped.CheckOnceAsync(Tea, false) is null, "It shares the receive cap, so the thirteenth in a minute is refused");
        }

        // ---- Check, Explain, Show me, Stories, Remember: gone after their time, seeds left alone ----
        {
            var now = DateTimeOffset.Now;
            var guardian = new MockGuardianService(new MailStore());
            var guide = new GuideStub();
            var api = new MockBackstopApi(guardian, new MockApiOptions(), null, null, new ExplainStub(), null, new StoryStub(), guide) { DisposalClock = () => now };
            var seededCards = (await api.GetMemoryCardsAsync()).Count;
            var seededActivity = (await api.GetActivityAsync()).Count;

            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];
            var check = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "My neighbour's bank letter arrived today.", null));
            var made = (await api.GenerateGuideAsync(new GuideRequest("set the washer", png, "a.png")))!;
            var letter = await api.ExplainContentAsync(new ExplainRequest("The words of the letter.", null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            var story = await api.MakeStoryAsync(new StoryRequest("Going to the dentist", null));
            var card = await api.SaveExplanationToMemoryAsync(letter.Id);
            await api.AskForHelpAsync(made.Id, 1);

            Assert(guide.Photo is not null, "Show me: the photo goes to the AI");
            Assert(check.Status == ReviewStatus.Held && api.GetHelperDigest().Items.Any(i => i.Id == check.Id), "No checker: the check is held for the helper");

            now = now.Add(MockBackstopApi.VisitorDataLife - TimeSpan.FromMinutes(1));
            Assert(api.DisposeExpiredVisitorData() == 0 && await api.GetCheckAsync(check.Id) is not null && await api.GetExplanationAsync(letter.Id) is not null, "Before the time is up nothing goes, so the visitor can read their answer");

            now += TimeSpan.FromMinutes(2);
            Assert(api.DisposeExpiredVisitorData() > 0, "After the time, it goes");
            Assert(await api.GetCheckAsync(check.Id) is null, "The check is gone");
            Assert(await api.GetGuideAsync(made.Id) is null, "The guide is gone");
            Assert(await api.GetExplanationAsync(letter.Id) is null, "The explanation is gone");
            Assert(await api.GetStoryAsync(story.Id) is null, "The story is gone");
            Assert(await api.GetMemoryCardAsync(card.Id) is null, "The saved card is gone");
            Assert(api.GetHelperDigest().Items.All(i => i.Id != check.Id && i.Kind != HelperItemKind.Help || i.Id == "h-seed"), "The helper's queue no longer lists any of it");
            Assert((await api.GetMemoryCardsAsync()).Count == seededCards, "Seeded cards stay");
            var log = await api.GetActivityAsync();
            Assert(log.Count == seededActivity && log.All(a => !a.Text.Contains("A letter")), "The activity log keeps only seeded lines");
            Assert(api.DisposeExpiredVisitorData() == 0, "A second sweep finds nothing");
        }

        // ---- A safeguarding alert stays open, without the person's words ----
        {
            var now = DateTimeOffset.Now;
            var api = new MockBackstopApi(new MockGuardianService(new MailStore()), new MockApiOptions()) { DisposalClock = () => now };
            var risk = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "I am scared of him and my account number is 12345678.", null));
            Assert(risk.Flags.Any(f => f.Id == "at-risk"), "The local rules raise the alert");

            now += MockBackstopApi.VisitorDataLife + TimeSpan.FromMinutes(1);
            api.DisposeExpiredVisitorData();
            var item = api.GetHelperDigest().Items.FirstOrDefault(i => i.Id == risk.Id);
            Assert(item is { Kind: HelperItemKind.Safeguarding }, "The helper is still told someone may need help");
            Assert(item!.Preview.Length == 0, "The person's words are gone from the alert");
            var kept = await api.GetCheckAsync(risk.Id);
            Assert(kept is not null && kept.DisplayText.Length == 0 && !kept.Reason.Contains("12345678"), "Nothing of them is left on the record");
            await api.ResolveReviewItemAsync(risk.Id, ReviewDecision.Release);
            Assert(api.GetHelperDigest().Items.All(i => i.Id != risk.Id), "The helper can still close it");
        }

        Console.WriteLine("PASS: Visitor data: own emails are never stored, and what goes to Meta AI is disposed of after its time.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
