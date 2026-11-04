#if DEBUG
using System.Net;
using Backstop.Models;
using Backstop.Services;
using Backstop.Services.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backstop.Tests;

/// <summary>
/// Safeguarding and the features that use the AI: the helper is told before and whatever the AI does, nothing is called
/// safe without being read, and what a person typed is not ignored. Called from MetaScamCheckerTests.RunAllTests.
/// </summary>
public static class AiSafeguardTests
{
    private const string Danger = "my son hits me and I am scared of him";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];

    private sealed class Assistant(Func<string, AssistantReply> answer) : IAssistant
    {
        public Action? OnCall;
        public Task<AssistantReply> ReplyAsync(IReadOnlyList<AssistantTurn> history, string message, AssistantMaterial? material = null, CancellationToken ct = default)
        {
            OnCall?.Invoke();
            return Task.FromResult(answer(message));
        }
    }

    private sealed class Meta(MetaScamAnalysis answer) : IMetaScamChecker
    {
        public string? Sent;
        public Task<MetaScamAnalysis> CheckScamAsync(string? text, byte[]? imageBytes = null, string? mimeType = null, CancellationToken ct = default)
        {
            Sent = text;
            return Task.FromResult(answer);
        }
    }

    private sealed class Story(StoryDraft draft) : IStoryWriter
    {
        public Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default) => Task.FromResult(draft);
    }

    private sealed class GuideStub(GuideDraft draft) : IGuideWriter
    {
        public Task<GuideDraft> WriteAsync(string goal, byte[]? photo, string? mimeType, CancellationToken ct = default) => Task.FromResult(draft);
    }

    private sealed class Explainer(DocumentReading reading, DocumentAnswer? answer = null) : IDocumentExplainer
    {
        public Task<DocumentReading> ExplainAsync(string? text, byte[]? image, string? mimeType, ReadingLevel level, ExplainFormat format, CancellationToken ct = default) => Task.FromResult(reading);
        public Task<DocumentAnswer> AnswerAsync(string documentText, string question, CancellationToken ct = default) =>
            Task.FromResult(answer ?? new DocumentAnswer(true, "It is about your bin day.", false));
    }

    private sealed class Reply(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private static DocumentReading Reading(string text = "", bool needsHelper = false) =>
        new(true, "letter", "Bin letter", "It is about your bin day.", null, [new ExplainChunk("What it says", "Your bin day is changing.")], [], [], text, false, NeedsHelper: needsHelper);

    private static MockBackstopApi Api(IAssistant? assistant = null, IMetaScamChecker? meta = null, IDocumentExplainer? explainer = null,
        IStoryWriter? story = null, IGuideWriter? guide = null, MailStore? store = null)
    {
        store ??= new MailStore();
        return new MockBackstopApi(new MockGuardianService(store), new MockApiOptions(), meta, assistant, explainer, new MockMailService(store), story, guide);
    }

    private static List<HelperItem> Alerts(MockBackstopApi api) => api.GetHelperDigest().Items.Where(i => i.Kind == HelperItemKind.Safeguarding).ToList();
    private static List<HelperItem> Queued(MockBackstopApi api) => api.GetHelperDigest().Items.Where(i => i.Kind is HelperItemKind.Safeguarding or HelperItemKind.AskMe or HelperItemKind.Check).ToList();

    public static async Task RunAsync()
    {
        var fail = new AssistantReply("", false, FailureNote: "down", Failure: new FailureInfo("Please try again in a minute.", "Code: DOWN"));
        var fine = new AssistantReply("That sounds fine.", true);

        // ---- Ask Me tells the helper first, once, and when the AI fails ----
        {
            MockBackstopApi api = null!;
            var alertsWhenAiAsked = -1;
            var ai = new Assistant(_ => fine) { OnCall = () => alertsWhenAiAsked = Alerts(api).Count };
            api = Api(ai);
            Assert((await api.AskAsync([], Danger)).HelperTold, "A sign of danger tells the helper");
            Assert(alertsWhenAiAsked == 1, "The helper is told before the AI is asked");
            Assert(Alerts(api).Count == 1, "The helper is told once, not twice");

            var both = Api(new Assistant(_ => new AssistantReply("I am so sorry. I have told your helper.", true, NeedsHuman: true, AtRisk: true)));
            await both.AskAsync([], Danger);
            Assert(Alerts(both).Count == 1, "The AI agreeing does not make a second alert");

            var aiOnly = Api(new Assistant(_ => new AssistantReply("I have told your helper.", true, AtRisk: true)));
            await aiOnly.AskAsync([], "things are hard at home");
            Assert(Alerts(aiOnly).Count == 1, "The AI alone can raise the alert");

            var down = Api(new Assistant(_ => fail));
            var failed = await down.AskAsync([], "They told me to read out the code on the back");
            Assert(failed.HelperTold && Queued(down).Count == 1, "The AI failing on something worrying goes to the helper");

            var downPlain = Api(new Assistant(_ => fail));
            var plain = await downPlain.AskAsync([], "What is the weather like");
            Assert(!plain.HelperTold && Queued(downPlain).Count == 0, "The AI failing on an ordinary question does not trouble the helper");

            var noAi = Api();
            Assert((await noAi.AskAsync([], Danger)).HelperTold && Alerts(noAi).Count == 1, "With no AI at all, the helper is still told");
        }

        // ---- Ask Me: fixed caution on money, and a person for a strong sign or a reported scam ----
        {
            var api = Api(new Assistant(_ => new AssistantReply("That is kind of you.", true)));
            var grandson = await api.AskAsync([], "Should I send my grandson £500, he lost his phone?");
            Assert(grandson.Text.StartsWith("Please wait before you send money"), "A request for money gets fixed words, whatever the AI wrote");
            Assert(grandson.HelperTold && Queued(api).Count == 1, "A strong sign goes to the helper");

            var mild = Api(new Assistant(_ => fine));
            var raffle = await mild.AskAsync([], "I paid £5 for a raffle ticket");
            Assert(raffle.Text == "That sounds fine." && !raffle.HelperTold, "A price on its own gets no warning");

            var scammed = Api(new Assistant(_ => fine));
            Assert((await scammed.AskAsync([], "I think I’ve been scammed")).HelperTold && Queued(scammed).Count == 1, "Saying you were scammed goes to the helper");
        }

        // ---- Ask Me: what a message is asking for ----
        {
            Assert(AskIntent.Route("Is this safe?", false, false) == AskRoute.AskWhatToCheck, "Is this safe, with nothing pasted: asks what to check");
            Assert(AskIntent.Route("Is this email a scam?", false, false) == AskRoute.AskWhatToCheck, "Is this email a scam, with nothing open: asks what to check");
            Assert(AskIntent.Route("Is this safe?", false, true) == AskRoute.CheckOpenEmail, "With an email open, it means that email");
            Assert(AskIntent.Route("I think I've been scammed", false, false) == AskRoute.Talk, "Been scammed is a conversation, not a check");
            Assert(AskIntent.Route("I think I’ve been scammed", false, true) == AskRoute.Talk, "A curly apostrophe changes nothing");
            Assert(AskIntent.Route("Is this a scam? Buy gift cards and send me the codes", false, false) == AskRoute.CheckText, "A pasted message is checked");
            Assert(AskIntent.Route("is this safe https://parcel.example.net/pay", false, false) == AskRoute.CheckText, "A pasted link is checked");
            Assert(AskIntent.Route("What time is the bus?", false, false) == AskRoute.Talk, "An ordinary question is a conversation");
            Assert(AskIntent.Route("What does this say?", true, false) == AskRoute.ExplainPicture, "Words about reading a picture are not ignored");
            Assert(AskIntent.Route("", true, false) == AskRoute.CheckPicture && AskIntent.Route("Is this safe?", true, false) == AskRoute.CheckPicture, "Any other picture is checked");
        }

        // ---- Stories, Show me, Explain and questions all alert, before the AI and when it fails ----
        {
            var storyFails = Api(story: new Story(new StoryDraft(false, "", [], false, new FailureInfo("Please wait 40 seconds.", "Code: LIMIT"))));
            var said = "";
            try { await storyFails.MakeStoryAsync(new StoryRequest($"a story about how {Danger}", null)); }
            catch (BackstopApiException ex) { said = ex.Message; }
            Assert(Alerts(storyFails).Count == 1, "Story: the helper is told even when the AI fails");
            Assert(said == "Please wait 40 seconds.", "Story: the real reason is given");

            var noWriter = Api();
            try { await noWriter.MakeStoryAsync(new StoryRequest(Danger, null)); } catch (BackstopApiException) { }
            Assert(Alerts(noWriter).Count == 1, "Story: the helper is told with no AI at all");

            var refused = Api(guide: new GuideStub(new GuideDraft(true, true, "", [], null, GuidePictures.Fallback, [], "")));
            Assert(await refused.GenerateGuideAsync(new GuideRequest(Danger, null, null)) is null, "Show me: the goal is refused");
            Assert(Alerts(refused).Count == 1, "Show me: a refused goal with danger in it still tells the helper");
            var plainRefused = Api(guide: new GuideStub(new GuideDraft(true, true, "", [], null, GuidePictures.Fallback, [], "")));
            await plainRefused.GenerateGuideAsync(new GuideRequest("rewire the plug", null, null));
            Assert(Queued(plainRefused).Count == 0, "Show me: an ordinary refused goal does not");

            var explain = Api(explainer: new Explainer(Reading()));
            await explain.ExplainContentAsync(new ExplainRequest(Danger, null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(Alerts(explain).Count == 1, "Explain: typed words are scanned");

            var photo = Api(explainer: new Explainer(Reading(Danger)));
            await photo.ExplainContentAsync(new ExplainRequest(null, Png, "a.png", ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(Alerts(photo).Count == 1, "Explain: the words read from a photo are scanned");

            var q = Api(explainer: new Explainer(Reading(), new DocumentAnswer(false, "", false, new FailureInfo("Please try again.", "Code: DOWN"))));
            var letter = await q.ExplainContentAsync(new ExplainRequest("Your bin day is changing.", null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            try { await q.AskAboutAsync(letter.Id, Danger); } catch (BackstopApiException) { }
            Assert(Alerts(q).Count == 1, "A question about a document is scanned, even when the AI fails");
        }

        // ---- needsHelper from Explain: read, and honoured with the title only ----
        {
            var content = "{\"title\":\"Bin letter\",\"overview\":\"It is about your bin day.\",\"chunks\":[{\"heading\":\"What it says\",\"body\":\"Your bin day is changing.\"}],\"suspicious\":false,\"needsHelper\":true}";
            var outer = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["META_AI_KEY"] = "test-key" }).Build();
            var real = new MetaDocumentExplainer(new HttpClient(new Reply(outer)), config, NullLogger<MetaDocumentExplainer>.Instance);
            Assert((await real.ExplainAsync("Your bin day is changing.", null, null, ReadingLevel.Simple, ExplainFormat.Summary)).NeedsHelper, "needsHelper is read from the reply");

            const string secret = "Your account number at the council is on file";
            var api = Api(explainer: new Explainer(Reading(needsHelper: true)));
            var e = await api.ExplainContentAsync(new ExplainRequest(secret, null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(e.Overview.Contains(api.Household.TrustedPersonName), "The person is told who can look at it with them");
            var asks = api.GetHelperDigest().Items.Where(i => i.Kind == HelperItemKind.Help && i.Detail == "Bin letter").ToList();
            Assert(asks.Count == 1, "The helper is asked to look");
            Assert(api.GetHelperDigest().Items.All(i => !i.Preview.Contains(secret) && !i.Detail.Contains(secret)), "The helper gets the title, not the document");
            await api.ExplainContentAsync(new ExplainRequest(secret, null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(api.GetHelperDigest().Items.Count(i => i.Kind == HelperItemKind.Help && i.Detail == "Bin letter") == 1, "The same title is not asked twice");
        }

        // ---- Check: the AI sees every address, and what it did not read is never safe ----
        {
            var safe = new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true);
            var meta = new Meta(safe);
            var api = Api(meta: meta);
            var parcel = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "Your parcel is waiting https://parcel-fee.example.net/pay"));
            Assert(meta.Sent!.Contains("hxxps://parcel-fee[.]example[.]net/pay") && !meta.Sent.Contains("[link removed]"), "Check: the AI gets the address, made harmless");
            Assert(parcel.DisplayText.Contains("[link removed]") && !parcel.DisplayText.Contains("parcel-fee"), "Check: the person still sees no address");

            await api.CheckContentAsync(new CheckRequest(CheckKind.Conversation, "Sam: See https://parcel-fee.example.net/pay"));
            Assert(meta.Sent!.Contains("hxxps://parcel-fee[.]example[.]net/pay"), "Conversation: the AI gets the address too");

            var longText = string.Concat(Enumerable.Repeat("See you at the cafe on Friday. ", 400));
            var cut = await api.CheckContentAsync(new CheckRequest(CheckKind.Message, longText));
            Assert(cut.Verdict != Verdict.LooksSafe, "Too long to read in full: never Looks safe");
            Assert((await api.CheckContentAsync(new CheckRequest(CheckKind.Message, "See you at the cafe."))).Verdict == Verdict.LooksSafe, "A short clean message can still be Looks safe");
        }

        // ---- Pictures: too big is refused up front, and the helper is told none was kept ----
        {
            var big = new byte[MetaAiScamChecker.MaxImageBytes + 1];
            Png.CopyTo(big, 0);
            var api = Api(meta: new Meta(new MetaScamAnalysis(Verdict.LooksSafe, "Fine.", [], true)));
            var refused = "";
            try { await api.CheckContentAsync(new CheckRequest(CheckKind.Picture, null, big, "big.png")); }
            catch (BackstopApiException ex) { refused = ex.Message; }
            Assert(refused.Contains("too big"), "A picture over the limit gets a plain error");
            Assert(Queued(api).Count == 0, "A refused picture puts nothing in the helper's queue");

            var noAi = Api();
            var held = await noAi.CheckContentAsync(new CheckRequest(CheckKind.Picture, null, Png, "a.png"));
            Assert(held.Status == ReviewStatus.Held, "No AI: the picture is held");
            Assert((await noAi.GetReviewItemAsync(held.Id))!.Preview == "The picture was not kept.", "The helper is told it was not kept");

            var withWords = await noAi.CheckContentAsync(new CheckRequest(CheckKind.Picture, Danger, Png, "a.png"));
            Assert(withWords.Flags.Any(f => f.Id == "at-risk"), "Words typed with a picture go through the local checks");
            Assert((await noAi.GetReviewItemAsync(withWords.Id))!.Preview.Contains("scared of him"), "The helper sees those words");
        }

        // ---- Conversations: the person's own lines count for their safety, not as scam signs ----
        {
            var api = Api();
            var r = await api.CheckContentAsync(new CheckRequest(CheckKind.Conversation, "Sam: How are you?\nMe: I am scared of him and he hits me"));
            Assert(r.Flags.Any(f => f.Id == "at-risk"), "\"Me:\" lines are scanned for danger");
            var mine = await api.CheckContentAsync(new CheckRequest(CheckKind.Conversation, "Sam: How are you?\nMe: I bought a gift card today"));
            Assert(mine.Flags.All(f => f.Id != "money"), "\"Me:\" lines are still not scam signs");
        }

        // ---- The at-risk flag is never one of those left out ----
        {
            var api = Api();
            var r = await api.CheckContentAsync(new CheckRequest(CheckKind.Message,
                "Buy a gift card, keep this between us, this is urgent, this is my new number, use the other app, install anydesk, give your PIN. I am scared of him."));
            Assert(r.Flags.Count == 4 && r.Flags.Any(f => f.Id == "at-risk"), "Only four flags are kept, and at-risk is one of them");
            Assert(Alerts(api).Any(i => i.Id == r.Id), "It still shows as an urgent alert");
        }

        // ---- Drafts: whole names only ----
        {
            async Task<string?> DraftTo(string name)
            {
                var api = Api(new Assistant(_ => new AssistantReply("Here is a draft.", true, Draft: new AssistantDraft(name, "Hello", "Hello there."))));
                return (await api.AskAsync([], "Write a note")).Draft?.To;
            }
            Assert(await DraftTo("Priya") == "priya.sandhu@example.com", "A first name finds the contact");
            Assert(await DraftTo("Priya Sandhu") == "priya.sandhu@example.com", "A full name finds the contact");
            Assert(await DraftTo("Pri") is null, "Part of a name does not");
            Assert(await DraftTo("Sandhu") is null, "A name two contacts share gets no address");
        }

        // ---- Stories: one bad page does not lose the story, too many do ----
        {
            static string P(string text) => $"{{\"heading\":\"Step\",\"text\":\"{text}\",\"picture\":\"home\"}}";
            string Story1(params string[] pages) => $"{{\"title\":\"Going out\",\"pages\":[{string.Join(",", pages)}]}}";
            const string bad = "Go to http://example.com now.";
            var one = MetaStoryWriter.ParseContent(Story1(P("First I go."), P(bad), P("Then I wait."), P("Then I go home.")));
            Assert(one.Success && one.Pages.Count == 3, "One unsafe page is left out");
            Assert(one.Pages.All(p => !p.Text.Contains("example.com")), "It is really gone");
            Assert(!MetaStoryWriter.ParseContent(Story1(P("First I go."), P(bad), P(bad))).Success, "Mostly unsafe pages: no story");
            Assert(!MetaStoryWriter.ParseContent(Story1(P(bad))).Success, "No safe pages: no story");
            Assert(MetaStoryWriter.ParseContent(Story1(P("First I go."))).Success, "A short clean story is fine");
        }

        // ---- Explain says when it only read part ----
        {
            var api = Api(explainer: new Explainer(Reading()));
            var longLetter = string.Concat(Enumerable.Repeat("Your bin day is changing soon. ", 400));
            var cut = await api.ExplainContentAsync(new ExplainRequest(longLetter, null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(cut.Chunks[0].Heading == "Only the first part", "A long document says it was only partly read");
            var whole = await api.ExplainContentAsync(new ExplainRequest("Your bin day is changing.", null, null, ReadingLevel.Simple, ExplainFormat.Summary));
            Assert(whole.Chunks.All(c => c.Heading != "Only the first part"), "A short one does not");
        }

        Console.WriteLine("PASS: AI features: the helper is told first and when the AI fails, links reach the AI, and nothing unread is called safe.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }
}
#endif
