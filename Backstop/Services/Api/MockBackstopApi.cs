using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

/// <summary>Demo switches for the mock. MOCK: there is no such thing in a real back end.</summary>
public sealed class MockApiOptions
{
    /// <summary>Every call fails after its delay, so each screen's error state can be seen.</summary>
    public bool FailRequests { get; set; }
}

/// <summary>
/// MOCK implementation of <see cref="IBackstopApi"/>. Everything is kept in memory (singleton) so the user and
/// trusted-person views see each other's changes live. Each call waits a little, like a real network call.
/// Replace this class (and its registration in Program.cs) to wire the real back end.
/// </summary>
public sealed partial class MockBackstopApi(IGuardianService guardian, MockApiOptions options, ILlamaScamChecker? llamaChecker = null, IAssistant? assistant = null, IDocumentExplainer? documentExplainer = null, IMailService? mail = null) : IBackstopApi
{
    private sealed record StoredCheck(CheckResult Result, string Original, DateTimeOffset? AskedAt);

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredCheck> _checks = [];
    private readonly Dictionary<string, (Guide Guide, GuideFixtures.GuideTask Task)> _guides = [];
    private readonly Dictionary<string, Explanation> _explanations = [];
    private readonly Dictionary<string, ContactPhoto> _photos = [];
    private readonly Dictionary<string, string> _savedFrom = []; // guide or explanation id -> memory id
    private List<MemoryCard> _memories = MemoryFixtures.Seed();
    private List<ActivityEntry> _activity = SeedActivity();

    private const int MaxCheckLength = 20000;
    private const int MaxExplainImageBytes = 8 * 1024 * 1024;

    public event Action? Changed;

    public Household Household => HouseholdFixture.Household;
    private string User => Household.UserName;
    private string Helper => Household.TrustedPersonName;

    // ---------------- Check ----------------

    public async Task<CheckResult> CheckContentAsync(CheckRequest request, CancellationToken ct = default)
    {
        if (request.Kind == CheckKind.Conversation) return await GetConversationAnalysisAsync(request.Text ?? "", ct);
        await Wait(400, ct);

        string text;
        string? imgType = null;
        var isPicture = request.Kind == CheckKind.Picture;
        if (isPicture)
        {
            if (request.Picture is null || (imgType = ImageSniff.ImageType(request.Picture)) is null)
                throw new BackstopApiException("We could not read that file. Please choose a photo or a screenshot.");
            text = ""; // Only the AI can read a picture. No local text stands in for it.
        }
        else text = request.Text ?? "";
        if (text.Length > MaxCheckLength) throw new BackstopApiException("That is too long to check. Please paste a shorter part.");
        if (string.IsNullOrWhiteSpace(text) && !isPicture) throw new BackstopApiException("Please paste the message first.");

        var display = Defang.ForUser(text.Trim());

        // Simple local checks as the floor:
        var floor = MockRiskRules.Scan(display);
        // The text shown has its links taken out, so the addresses are judged from the text as written.
        var links = LinkFinder.Inspect(text);
        var floorScore = floor.Score + (links.Problems > 0 ? 1 : 0);
        var floorVerdict = MockRiskRules.VerdictFor(floorScore);
        var combinedFlags = new List<RiskFlag>(floor.Flags);
        var floorBecause = floor.TopBecause;
        if (links.Problems > 0)
        {
            combinedFlags.Add(links.Deceptive
                ? new RiskFlag("link-hidden", "link", "The link hides where it goes", "The address is disguised or shortened, so you cannot tell which website it leads to.")
                : new RiskFlag("link-odd", "link", "The link looks unusual", "The address has words or a form that real senders rarely use."));
            if (floorBecause is null || floorBecause == MockRiskRules.BecauseFor("link"))
                floorBecause = links.Deceptive ? "its link hides where it goes" : "its link looks unusual";
        }

        // If checking a picture, send image bytes to the multimodal AI endpoint;
        // otherwise, send text only to save vision processing costs.
        var (finalVerdict, finalReason, reviewStatus, failure) = await ApplyAiAsync(
            isPicture ? c => c.CheckScamAsync(null, request.Picture, imgType, ct) : c => c.CheckScamAsync(display, null, null, ct),
            aiRequired: isPicture, floorVerdict, MockRiskRules.ReasonFor(floorVerdict, floorBecause), combinedFlags);

        var flags = combinedFlags.Take(4).ToList();
        var result = new CheckResult(NewId("chk"), request.Kind, finalVerdict, finalReason, flags,
            display, floor.Highlights.Where(h => flags.Any(f => f.Id == h.FlagId)).ToList(), null, reviewStatus, false, DateTimeOffset.Now, failure);
        lock (_gate) _checks[result.Id] = new(result, text.Trim(), reviewStatus == ReviewStatus.Held ? DateTimeOffset.Now : null);
        return result;
    }

    /// <summary>
    /// The one place the AI result meets the local floor. The AI can only make the verdict stricter.
    /// Any failure, exception or odd answer holds the item. When <paramref name="aiRequired"/> is set
    /// (pictures, which local rules cannot read) a missing checker also holds it.
    /// </summary>
    private async Task<(Verdict Verdict, string Reason, ReviewStatus Status, FailureInfo? Failure)> ApplyAiAsync(
        Func<ILlamaScamChecker, Task<MetaScamAnalysis>> ask, bool aiRequired,
        Verdict floorVerdict, string floorReason, List<RiskFlag> flags)
    {
        var r = await ApplyAiCoreAsync(ask, aiRequired, floorVerdict, floorReason, flags);

        // Someone may be in danger, or the checker wants a human: the helper is always told, whatever else was decided.
        // The local rules and the AI can both raise this. Nothing can lower it.
        if (r.Status != ReviewStatus.Held && flags.Any(f => f.Id is "at-risk" or "review-needed"))
            return (IsStricter(Verdict.BeCareful, r.Verdict) ? Verdict.BeCareful : r.Verdict, r.Reason, ReviewStatus.Held, r.Failure);
        return r;
    }

    private async Task<(Verdict Verdict, string Reason, ReviewStatus Status, FailureInfo? Failure)> ApplyAiCoreAsync(
        Func<ILlamaScamChecker, Task<MetaScamAnalysis>> ask, bool aiRequired,
        Verdict floorVerdict, string floorReason, List<RiskFlag> flags)
    {
        if (llamaChecker is null)
        {
            if (!aiRequired) return (floorVerdict, floorReason, ReviewStatus.NotAsked, null);
            return HoldForReview(flags, floorVerdict, "We could not check this picture, so your helper will look at it.", new("The picture checker is not switched on yet. Please tell the person who looks after this app.", "Code: NO_CHECKER\nWhat: No IMetaScamChecker is registered."));
        }

        MetaScamAnalysis ai;
        try { ai = await ask(llamaChecker); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return HoldForReview(flags, floorVerdict, "We could not finish checking this, so your helper will look at it.", new("Something went wrong while checking. Please try again in a minute.", $"Code: CHECK_EXCEPTION\nWhat: {ex.GetType().Name}\nTime: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"));
        }

        foreach (var flag in ai.Flags)
            if (flags.All(f => f.Id != flag.Id)) flags.Add(flag);

        if (!ai.Success)
        {
            var stricter = IsStricter(Verdict.BeCareful, floorVerdict) ? Verdict.BeCareful : floorVerdict;
            return (stricter, ai.Reason, ReviewStatus.Held, ai.Failure);
        }
        return IsStricter(ai.Verdict, floorVerdict)
            ? (ai.Verdict, ai.Reason, ReviewStatus.NotAsked, null)
            : (floorVerdict, floorReason, ReviewStatus.NotAsked, null);
    }

    private static (Verdict, string, ReviewStatus, FailureInfo?) HoldForReview(List<RiskFlag> flags, Verdict floorVerdict, string reason, FailureInfo? failure)
    {
        if (flags.All(f => f.Id != "review-needed"))
            flags.Add(new("review-needed", "hourglass", "Needs helper check", "The automatic check could not finish, so your helper will check this."));
        return (IsStricter(Verdict.BeCareful, floorVerdict) ? Verdict.BeCareful : floorVerdict, reason, ReviewStatus.Held, failure);
    }

    private static bool IsStricter(Verdict a, Verdict b)
    {
        int Rank(Verdict v) => v switch
        {
            Verdict.DontReply => 3,
            Verdict.BeCareful => 2,
            _ => 1
        };
        return Rank(a) > Rank(b);
    }

    public async Task<CheckResult> GetConversationAnalysisAsync(string conversation, CancellationToken ct = default)
    {
        await Wait(600, ct);
        if (string.IsNullOrWhiteSpace(conversation)) throw new BackstopApiException("Please paste the conversation first.");
        if (conversation.Length > MaxCheckLength) throw new BackstopApiException("That is too long to check. Please paste a shorter part.");

        var display = Defang.ForUser(conversation.Trim());
        var thread = new List<ThreadMessage>();
        var seen = new List<RiskFlag>();
        var running = 0;
        var firstFlagged = -1;
        foreach (var (sender, text) in ParseThread(display))
        {
            var fromUser = sender.Equals("Me", StringComparison.OrdinalIgnoreCase) || sender.Equals(User, StringComparison.OrdinalIgnoreCase);
            var found = fromUser ? MockRiskRules.Scan("") : MockRiskRules.Scan(text);
            foreach (var f in found.Flags)
            {
                running += MockRiskRules.Weight(f.Id);
                if (seen.All(s => s.Id != f.Id)) seen.Add(f);
            }
            if (found.Flags.Count > 0 && firstFlagged < 0) firstFlagged = thread.Count;
            var risk = Math.Min(100, running * 11);
            var level = risk < 25 ? RiskLevel.Calm : risk < 60 ? RiskLevel.Rising : RiskLevel.High;
            thread.Add(new(sender, fromUser, text, risk, level, found.Flags.Select(f => f.Id).ToList(), found.Highlights));
        }

        var floorVerdict = MockRiskRules.VerdictFor(seen.Sum(f => MockRiskRules.Weight(f.Id)));
        var combinedFlags = new List<RiskFlag>(seen.OrderByDescending(f => MockRiskRules.Weight(f.Id)));

        var (finalVerdict, finalReason, reviewStatus, failure) = await ApplyAiAsync(
            c => c.CheckScamAsync(display, null, null, ct), aiRequired: false, floorVerdict,
            MockRiskRules.ReasonFor(floorVerdict, MockRiskRules.BecauseFor(combinedFlags.FirstOrDefault()?.Id), startedFriendly: firstFlagged > 0),
            combinedFlags);

        var flags = combinedFlags.Take(4).ToList();
        var result = new CheckResult(NewId("chk"), CheckKind.Conversation, finalVerdict,
            finalReason,
            flags, display, [], thread, reviewStatus, false, DateTimeOffset.Now, failure);
        lock (_gate) _checks[result.Id] = new(result, conversation.Trim(), reviewStatus == ReviewStatus.Held ? DateTimeOffset.Now : null);
        return result;
    }

    public async Task<CheckResult?> GetCheckAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _checks.GetValueOrDefault(id)?.Result;
    }

    public async Task<IReadOnlyList<CheckResult>> GetAskedChecksAsync(CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
            return _checks.Values.Where(c => c.AskedAt is not null).OrderByDescending(c => c.AskedAt).Select(c => c.Result).ToList();
    }

    public async Task<CheckResult> AskTrustedPersonAsync(string checkId, CancellationToken ct = default)
    {
        await Wait(600, ct);
        CheckResult result;
        lock (_gate)
        {
            var stored = _checks.GetValueOrDefault(checkId) ?? throw new BackstopApiException("We could not find that check. Please check it again.");
            // Fail closed: once asked, it stays held until the trusted person decides.
            result = stored.Result.Status == ReviewStatus.NotAsked ? stored.Result with { Status = ReviewStatus.Held } : stored.Result;
            _checks[checkId] = stored with { Result = result, AskedAt = stored.AskedAt ?? DateTimeOffset.Now };
            Log("send", $"{User} asked you to look at {LowerFirst(TitleFor(result, stored.Original))}.");
        }
        Changed?.Invoke();
        return result;
    }

    public async Task DeleteCheckAsync(string checkId, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) _checks.Remove(checkId);
        Changed?.Invoke();
    }

    // ---------------- Show me ----------------

    public async Task<Guide?> GenerateGuideAsync(GuideRequest request, CancellationToken ct = default)
    {
        await Wait(1600, ct);
        if (string.IsNullOrWhiteSpace(request.Goal)) throw new BackstopApiException("Please say what you want to do.");

        string? photoUrl = null;
        if (request.Photo is not null)
        {
            var type = ImageSniff.ImageType(request.Photo) ?? throw new BackstopApiException("We could not use that file. Please take a photo or choose a picture.");
            var photoId = NewId("p");
            lock (_gate) _photos[photoId] = new ContactPhoto(request.Photo, type);
            photoUrl = $"/mode-photo/{photoId}";
        }

        // MOCK: a real back end looks at the photo and writes the steps. Here the goal's words pick a task.
        var task = GuideFixtures.Match(request.Goal);
        if (task is null) return null;
        var guide = new Guide(NewId("g"), task.Title, request.Goal.Trim(), photoUrl, task.Picture, task.Steps,
            photoUrl is null ? null : task.Highlight, task.NeedsCare, DateTimeOffset.Now);
        lock (_gate) _guides[guide.Id] = (guide, task);
        return guide;
    }

    public async Task<Guide?> GetGuideAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _guides.TryGetValue(id, out var g) ? g.Guide : null;
    }

    public async Task AskForHelpAsync(string guideId, int stepNumber, CancellationToken ct = default)
    {
        await Wait(600, ct);
        lock (_gate)
        {
            var title = _guides.TryGetValue(guideId, out var g) ? g.Guide.Title : "a task";
            Log("hand", $"{User} is stuck on step {stepNumber} of \"{title}\" and would like your help.");
        }
        Changed?.Invoke();
    }

    // ---------------- Explain ----------------

    public async Task<Explanation> ExplainContentAsync(ExplainRequest request, CancellationToken ct = default)
    {
        string? typed = null, mime = null;
        if (request.File is not null)
        {
            if (ImageSniff.IsPdf(request.File))
                throw new BackstopApiException("We cannot read PDFs yet. Please take a photo of the page, or type the words.");
            mime = ImageSniff.ImageType(request.File);
            if (mime is null) throw new BackstopApiException("We could not read that file. Please choose a photo of the letter.");
            if (request.File.Length > MaxExplainImageBytes) throw new BackstopApiException("That photo is too big. Please take it again.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Text)) throw new BackstopApiException("Please paste the words first.");
            // Links come out first, so nothing clickable or hidden is sent on or shown.
            typed = Defang.ForUser(request.Text.Trim());
            if (typed.Length > MaxCheckLength) throw new BackstopApiException("That is too long to explain. Please paste a shorter part.");
        }

        if (documentExplainer is null)
            throw new BackstopApiException("Explain is not switched on yet. Please tell the person who looks after this app.");
        var reading = await documentExplainer.ExplainAsync(typed, request.File, mime, request.Level, request.Format, ct);
        if (!reading.Success)
            throw new BackstopApiException(reading.Failure?.Message ?? "Something went wrong. Please try again in a minute.");

        var explanation = BuildExplanation(reading, typed ?? Defang.ForUser(reading.Text), request.Level, request.Format);
        lock (_gate) _explanations[explanation.Id] = explanation;
        return explanation;
    }

    public async Task<Explanation?> GetExplanationAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _explanations.GetValueOrDefault(id);
    }

    public async Task<ChatAnswer> AskAboutAsync(string explanationId, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question)) throw new BackstopApiException("Please type your question first.");
        if (question.Length > 500) throw new BackstopApiException("That is a bit long. Please make your question shorter.");
        Explanation? e;
        lock (_gate) e = _explanations.GetValueOrDefault(explanationId);
        if (e is null) throw new BackstopApiException("We could not find that page. Please explain it again.");
        if (documentExplainer is null)
            throw new BackstopApiException("Questions are not switched on yet. Please tell the person who looks after this app.");

        var reply = await documentExplainer.AnswerAsync(e.OriginalText, Defang.ForUser(question.Trim()), ct);
        if (!reply.Success)
            throw new BackstopApiException(reply.Failure?.Message ?? "Something went wrong. Please try again in a minute.");
        var answer = reply.Text;
        if (reply.NeedsHelper && !answer.Contains("helper", StringComparison.OrdinalIgnoreCase) && !answer.Contains(Helper, StringComparison.OrdinalIgnoreCase))
            answer += $" {Helper} can look at it with you.";
        return new ChatAnswer(question.Trim(), answer);
    }

    // ---------------- Remember ----------------

    public IReadOnlyList<string> SuggestedMemoryQuestions => MemoryFixtures.SuggestedQuestions;

    public async Task<IReadOnlyList<MemoryCard>> GetMemoryCardsAsync(string? search = null, CancellationToken ct = default)
    {
        await Wait(500, ct);
        lock (_gate)
        {
            var cards = _memories.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search)) cards = cards.Where(c => Score(c, search) > 0);
            return cards.OrderByDescending(c => c.Date).ToList();
        }
    }

    public async Task<MemoryCard?> GetMemoryCardAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _memories.FirstOrDefault(c => c.Id == id);
    }

    public async Task<MemoryAnswer> AskMemoryAsync(string question, CancellationToken ct = default)
    {
        await Wait(900, ct);
        if (string.IsNullOrWhiteSpace(question)) throw new BackstopApiException("Please type your question first.");
        List<MemoryCard> best;
        lock (_gate)
        {
            var scored = _memories.Select(c => (Card: c, Score: Score(c, question))).Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.Card.Date).ToList();
            // A second card only when it matches nearly as well as the best one.
            var top = scored.FirstOrDefault().Score;
            best = scored.Where(x => x.Score * 10 >= top * 7).Take(2).Select(x => x.Card).ToList();
        }
        var answer = best.Count == 0
            ? "I could not find that in your memory yet. When you save a Show me or Explain page, you can ask about it here."
            : $"{best[0].HowYouDidIt} Here are the steps.";
        return new MemoryAnswer(question.Trim(), answer, best);
    }

    public async Task<MemoryCard> SaveGuideToMemoryAsync(string guideId, CancellationToken ct = default)
    {
        await Wait(500, ct);
        MemoryCard card;
        lock (_gate)
        {
            if (Existing(guideId) is { } saved) return saved;
            if (!_guides.TryGetValue(guideId, out var g)) throw new BackstopApiException("We could not find that guide. Please try Show me again.");
            var steps = g.Guide.Steps;
            card = new MemoryCard(NewId("m"), g.Guide.Title, Today, $"You asked: \"{g.Guide.Goal}\". {steps.Count} steps.",
                g.Guide.PhotoUrl, g.Task.Picture, g.Task.Tags, steps,
                $"Last time you {g.Task.PastTense} in {steps.Count} steps. The first one was: {LowerFirst(steps[0])}",
                MemorySource.ShowMe, false);
            AddMemory(guideId, card);
        }
        Changed?.Invoke();
        return card;
    }

    public async Task<MemoryCard> SaveExplanationToMemoryAsync(string explanationId, CancellationToken ct = default)
    {
        await Wait(500, ct);
        MemoryCard card;
        lock (_gate)
        {
            if (Existing(explanationId) is { } saved) return saved;
            var e = _explanations.GetValueOrDefault(explanationId) ?? throw new BackstopApiException("We could not find that page. Please explain it again.");
            card = new MemoryCard(NewId("m"), e.Title, Today, e.Overview, null, "letter", ["letters"],
                e.Actions.Count > 0 ? e.Actions : e.Chunks.Select(c => c.Body).Take(4).ToList(),
                $"Last time, the {LowerFirst(e.Title)} said this: {e.Overview}", MemorySource.Explain, false);
            AddMemory(explanationId, card);
        }
        Changed?.Invoke();
        return card;
    }

    public async Task<MemoryCard> RenameMemoryAsync(string id, string title, CancellationToken ct = default)
    {
        await Wait(400, ct);
        if (string.IsNullOrWhiteSpace(title)) throw new BackstopApiException("Please type a name.");
        var card = Replace(id, c => c with { Title = title.Trim() });
        Changed?.Invoke();
        return card;
    }

    public async Task<MemoryCard> ShareMemoryAsync(string id, bool shared, CancellationToken ct = default)
    {
        await Wait(500, ct);
        var card = Replace(id, c => c with { Shared = shared });
        lock (_gate)
            Log(shared ? "share" : "minus", shared ? $"{User} shared \"{card.Title}\" with you." : $"{User} stopped sharing \"{card.Title}\".");
        Changed?.Invoke();
        return card;
    }

    public async Task DeleteMemoryAsync(string id, CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
        {
            _memories.RemoveAll(c => c.Id == id);
            foreach (var key in _savedFrom.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) _savedFrom.Remove(key);
        }
        Changed?.Invoke();
    }

    // ---------------- Trusted person ----------------

    public async Task<IReadOnlyList<ReviewItem>> GetReviewQueueAsync(CancellationToken ct = default)
    {
        await Wait(600, ct);
        var inbox = guardian.GetQueue().Select(FromMail);
        List<ReviewItem> checks;
        List<ReviewItem> stories;
        lock (_gate)
        {
            checks = guardian.Allows(HelperCapability.SeeCheckRequests)
                ? _checks.Values.Where(c => c.Result.Status == ReviewStatus.Held).Select(FromCheck).ToList()
                : [];
            stories = _stories.Values.Where(x => x.Story.Status == StoryStatus.Held).Select(FromStory).ToList();
        }
        return inbox.Concat(checks).Concat(stories).OrderByDescending(i => i.At).ToList();
    }

    public async Task<ReviewItem?> GetReviewItemAsync(string id, CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
        {
            if (_checks.TryGetValue(id, out var c)) return c.Result.Status == ReviewStatus.Held && guardian.Allows(HelperCapability.SeeCheckRequests) ? FromCheck(c) : null;
            if (_stories.TryGetValue(id, out var st)) return st.Story.Status == StoryStatus.Held ? FromStory(st) : null;
        }
        var m = guardian.Get(id);
        return m is { State: MessageState.Held } ? FromMail(m) : null;
    }

    public async Task ResolveReviewItemAsync(string id, ReviewDecision decision, CancellationToken ct = default)
    {
        await Wait(500, ct);
        string title;
        var logged = false; // mail decisions are written to the activity log by the guardian service
        lock (_gate)
        {
            if (_checks.TryGetValue(id, out var c))
            {
                if (!guardian.Allows(HelperCapability.SeeCheckRequests)) throw new BackstopApiException($"{User} has not allowed you to see their checks.");
                // Only a held item can be decided, and Blocked is final.
                if (c.Result.Status != ReviewStatus.Held) throw new BackstopApiException("This has already been decided.");
                var r = decision switch
                {
                    ReviewDecision.Release => c.Result with { Status = ReviewStatus.Released, CallRequested = false },
                    ReviewDecision.Block => c.Result with { Status = ReviewStatus.Blocked, CallRequested = false },
                    _ => c.Result with { CallRequested = true },
                };
                _checks[id] = c with { Result = r };
                title = TitleFor(c.Result, c.Original);
            }
            else if (_stories.TryGetValue(id, out var st))
            {
                if (st.Story.Status != StoryStatus.Held) throw new BackstopApiException("This has already been decided.");
                title = $"the story about \"{Shorten(st.Situation)}\"";
                // Only Release lets the user read it. Block is final. Call first leaves it held.
                _stories[id] = st with
                {
                    Story = decision switch
                    {
                        ReviewDecision.Release => st.Story with { Status = StoryStatus.Ready },
                        ReviewDecision.Block => st.Story with { Status = StoryStatus.Blocked },
                        _ => st.Story,
                    },
                };
            }
            else
            {
                var m = guardian.Get(id);
                if (m is not { State: MessageState.Held }) throw new BackstopApiException("This has already been decided.");
                if (decision == ReviewDecision.Release && m.AwaitingScan) throw new BackstopApiException("The checker has not finished with this message yet.");
                title = $"\"{m.Subject}\"";
                logged = true;
                switch (decision)
                {
                    case ReviewDecision.Release: guardian.Approve(id); break;
                    case ReviewDecision.Block: guardian.Block(id); break;
                    default: guardian.CallFirst(id); break;
                }
            }
            if (!logged) Log(decision switch { ReviewDecision.Release => "check", ReviewDecision.Block => "cross", _ => "phone" },
                decision switch
                {
                    ReviewDecision.Release => $"You said {LowerFirst(title)} is safe.",
                    ReviewDecision.Block => $"You blocked {LowerFirst(title)}.",
                    _ => $"You will call {User} about {LowerFirst(title)}.",
                });
        }
        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(CancellationToken ct = default)
    {
        await Wait(400, ct);
        if (!guardian.Allows(HelperCapability.SeeActivitySummary)) return [];
        var limit = guardian.Allows(HelperCapability.SeeFullActivity) ? 50 : 10;
        lock (_gate) return _activity.Concat(guardian.GetMailActivity()).OrderByDescending(a => a.At).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<MemoryCard>> GetSharedMemoriesAsync(CancellationToken ct = default)
    {
        await Wait(400, ct);
        if (!guardian.Allows(HelperCapability.SeeSharedMemories)) return [];
        lock (_gate) return _memories.Where(c => c.Shared).OrderByDescending(c => c.Date).ToList();
    }

    public ContactPhoto? GetPhoto(string id)
    {
        lock (_gate) return _photos.GetValueOrDefault(id);
    }

    // ---------------- Demo support ----------------

    public void ResetDemo()
    {
        lock (_gate)
        {
            _checks.Clear(); _stories.Clear(); _guides.Clear(); _explanations.Clear(); _photos.Clear(); _savedFrom.Clear();
            _memories = MemoryFixtures.Seed();
            _activity = SeedActivity();
            ResetChat();
        }
        Changed?.Invoke();
    }

    public void ClearMemories()
    {
        lock (_gate) { _memories.Clear(); _savedFrom.Clear(); }
        Changed?.Invoke();
    }

    // ---------------- Helpers ----------------

    private async Task Wait(int ms, CancellationToken ct)
    {
#if !DEBUG
        ms = Math.Min(ms, 150); // the pauses imitate a slow back end. Release keeps them short.
#endif
        await Task.Delay(ms, ct);
        if (options.FailRequests) throw new BackstopApiException("We could not reach Backstop just now. Please try again in a moment.");
    }

    private static string NewId(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    // Caller holds the lock.
    private void Log(string icon, string text) => _activity.Add(new ActivityEntry(DateTimeOffset.Now, icon, text));

    private MemoryCard? Existing(string sourceId) =>
        _savedFrom.TryGetValue(sourceId, out var memoryId) ? _memories.FirstOrDefault(c => c.Id == memoryId) : null;

    private void AddMemory(string sourceId, MemoryCard card)
    {
        _memories.Add(card);
        _savedFrom[sourceId] = card.Id;
        Log("plus", $"{User} saved \"{card.Title}\" to Remember.");
    }

    private MemoryCard Replace(string id, Func<MemoryCard, MemoryCard> change)
    {
        lock (_gate)
        {
            var i = _memories.FindIndex(c => c.Id == id);
            if (i < 0) throw new BackstopApiException("We could not find that memory. It may have been deleted.");
            return _memories[i] = change(_memories[i]);
        }
    }

    private static int Score(MemoryCard card, string question)
    {
        var haystack = $"{card.Title} {string.Join(' ', card.Tags)} {card.Summary} {string.Join(' ', card.Steps)}".ToLowerInvariant();
        var terms = Words().Matches(question.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3 && !StopWords.Contains(w)).ToHashSet();
        foreach (var (key, also) in MemoryFixtures.Synonyms)
            if (terms.Contains(key) || also.Any(terms.Contains)) { terms.Add(key); terms.UnionWith(also); }
        return terms.Count(haystack.Contains);
    }

    private static readonly HashSet<string> StopWords = ["how", "did", "the", "and", "what", "does", "say", "said", "last", "time", "you", "can", "for", "with", "this", "that", "was", "again"];

    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex Words();

    [GeneratedRegex(@"^\s*([\p{L}][\p{L} .'-]{0,30}?):\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SenderLine();

    /// <summary>"Name: text" per line. Without names, each paragraph is one message from "Them".</summary>
    private static List<(string Sender, string Text)> ParseThread(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var list = new List<(string Sender, string Text)>();
        if (lines.Any(l => SenderLine().IsMatch(l)))
        {
            foreach (var line in lines.Where(l => l.Trim().Length > 0))
            {
                var m = SenderLine().Match(line);
                if (m.Success) list.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
                else if (list.Count > 0) list[^1] = (list[^1].Sender, $"{list[^1].Text} {line.Trim()}");
                else list.Add(("Them", line.Trim()));
            }
            return list;
        }
        return text.Replace("\r", "").Split("\n\n").Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => ("Them", p)).ToList();
    }

    private string TitleFor(CheckResult r, string original) => r.Kind switch
    {
        CheckKind.Conversation => $"A chat with {r.Thread?.FirstOrDefault(m => !m.FromUser)?.Sender ?? "someone"}",
        CheckKind.Picture => $"A picture {User} checked",
        _ => $"A message {User} checked",
    };

    private ReviewItem FromCheck(StoredCheck c)
    {
        var r = c.Result;
        var signs = r.Flags.Count == 0 ? "We did not find clear warning signs." :
            $"We found {r.Flags.Count} warning sign{(r.Flags.Count == 1 ? "" : "s")}: {string.Join(", ", r.Flags.Select(f => LowerFirst(f.Title)))}.";
        var high = r.Thread?.Select((m, i) => (m.Level, Number: i + 1)).FirstOrDefault(x => x.Level == RiskLevel.High).Number ?? 0;
        var trend = high > 0 ? $" The risk rose across the chat: calm at first, high by message {high} of {r.Thread!.Count}." : "";
        var reasoning = $"{signs}{trend} {User} asked you to look before replying.";
        return new ReviewItem(r.Id, ReviewSource.Check, TitleFor(r, c.Original), Defang.ForHelper(c.Original), r.Verdict, r.Flags,
            reasoning, r.CallRequested, c.AskedAt ?? r.CheckedAt);
    }

    private static ReviewItem FromMail(MailMessage m) => new(
        m.Id, ReviewSource.Inbox, m.Subject, Defang.ForHelper(m.Body),
        m.Confidence == Confidence.High ? Verdict.DontReply : Verdict.BeCareful,
        m.Flags.Select(f => new RiskFlag(f.Title, f.Icon, f.Title, f.Explanation)).ToList(),
        m.Summary, m.CallRequested, m.ReceivedAt);

    private static List<ActivityEntry> SeedActivity()
    {
        var now = DateTimeOffset.Now;
        var u = HouseholdFixture.Household.UserName;
        return
        [
            new(now.AddDays(-12), "plus", $"{u} saved \"Resetting the internet box\" to Remember."),
            new(now.AddDays(-12), "share", $"{u} shared \"Resetting the internet box\" with you."),
            new(now.AddDays(-3), "share", $"{u} shared \"Council Tax letter\" with you."),
            new(now.AddHours(-5), "hand", $"{u} is stuck on step 4 of \"Setting the washing machine\" and would like your help."),
        ];
    }
}
