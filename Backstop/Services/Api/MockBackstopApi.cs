using System.Text.RegularExpressions;
using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

/// <summary>Demo switches for the mock. MOCK: there is no such thing in a real back end.</summary>
public sealed class MockApiOptions
{
    private const int MaxVisitors = 5000;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _failing = new();
    private volatile bool _shared;

    /// <summary>
    /// Every call fails after its delay, so each screen's error state can be seen. Kept per visitor (their address), so one
    /// visitor switching it on never breaks the demo for anyone else. With no visitor (tests, background work) one shared switch is used.
    /// </summary>
    public bool FailRequests
    {
        get => ClientIdentity.Current is { } v ? _failing.ContainsKey(v) : _shared;
        set
        {
            if (ClientIdentity.Current is not { } v) { _shared = value; return; }
            if (!value) { _failing.TryRemove(v, out _); return; }
            if (_failing.Count >= MaxVisitors) _failing.Clear();
            _failing[v] = 0;
        }
    }

    /// <summary>Switches it off for every visitor. Used by Reset demo.</summary>
    public void ResetAll()
    {
        _failing.Clear();
        _shared = false;
    }
}

/// <summary>
/// MOCK implementation of <see cref="IBackstopApi"/>. Everything is kept in memory (singleton) so the user and
/// trusted-person views see each other's changes live. Calls answer at once.
/// Replace this class (and its registration in Program.cs) to wire the real back end.
/// </summary>
public sealed partial class MockBackstopApi(IGuardianService guardian, MockApiOptions options, IMetaScamChecker? metaChecker = null, IAssistant? assistant = null, IDocumentExplainer? documentExplainer = null, IMailService? mail = null, IStoryWriter? storyWriter = null, IGuideWriter? guideWriter = null, IMemoryAnswerer? memoryAnswerer = null) : IBackstopApi
{
    private sealed record StoredCheck(CheckResult Result, string Original, DateTimeOffset? AskedAt);
    private sealed record StoredGuide(Guide Guide, IReadOnlyList<string> Tags, string PastTense);

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredCheck> _checks = [];
    private readonly Dictionary<string, StoredGuide> _guides = [];
    private readonly Dictionary<string, Explanation> _explanations = [];
    private readonly Dictionary<string, ContactPhoto> _photos = [];
    private readonly Dictionary<string, string> _savedFrom = []; // guide or explanation id -> memory id
    private List<MemoryCard> _memories = MemoryFixtures.Seed();
    private List<ActivityEntry> _activity = SeedActivity();
    private List<HelpRequest> _help = SeedHelp();

    private const int MaxCheckLength = 20000;
    private const int MaxExplainImageBytes = 8 * 1024 * 1024;
    private const int MaxFlagsShown = 4;

    public event Action? Changed;

    public Household Household => HouseholdFixture.Household;
    private string User => Household.UserName;
    private string Helper => Household.TrustedPersonName;

    // ---------------- Check ----------------

    public async Task<CheckResult> CheckContentAsync(CheckRequest request, CancellationToken ct = default)
    {
        if (request.Kind == CheckKind.Conversation) return await GetConversationAnalysisAsync(request.Text ?? "", ct);
        await Wait(400, ct);
        var generation = CurrentGeneration;

        string text;
        string? imgType = null;
        var isPicture = request.Kind == CheckKind.Picture;
        if (isPicture)
        {
            if (request.Picture is null || (imgType = ImageSniff.ImageType(request.Picture)) is null)
                throw new BackstopApiException("We could not read that file. Please choose a photo or a screenshot.");
            // Refused here, before any check is made, so nothing reaches the helper's queue.
            if (request.Picture.Length > MetaAiScamChecker.MaxImageBytes)
                throw new BackstopApiException("That picture is too big. Please choose a smaller one.");
            // Only the AI can read a picture. Words typed with it still go through the local checks.
            text = request.Text ?? "";
        }
        else text = request.Text ?? "";
        if (text.Length > MaxCheckLength) throw new BackstopApiException("That is too long to check. Please paste a shorter part.");
        if (string.IsNullOrWhiteSpace(text) && !isPicture) throw new BackstopApiException("Please paste the message first.");

        var display = Defang.ForUser(text.Trim());
        // The AI gets every address, made harmless, so it can judge where a link goes. The person sees "[link removed]".
        var forAi = Defang.ForHelper(text.Trim());

        // Simple local checks as the floor:
        var floor = MockRiskRules.Scan(display);
        // The link rule is left to the Link check below, so each link is counted once.
        // The text shown has its links taken out, so the addresses are judged from the text as written.
        var links = LinkFinder.Inspect(text);
        var floorFlags = floor.Flags.Where(f => f.Id != "link").ToList();
        var floorScore = floorFlags.Sum(f => MockRiskRules.Weight(f.Id)) + (links.Problems > 0 ? 1 : 0);
        var floorVerdict = MockRiskRules.VerdictFor(floorScore);
        var combinedFlags = new List<RiskFlag>(floorFlags);
        var floorBecause = floorFlags.OrderByDescending(f => MockRiskRules.Weight(f.Id)).Select(f => MockRiskRules.BecauseFor(f.Id)).FirstOrDefault();
        if (links.Problems > 0)
        {
            combinedFlags.Add(links.Deceptive
                ? new RiskFlag("link-hidden", "link", "The link hides where it goes", "The address is disguised or shortened, so you cannot tell which website it leads to.")
                : new RiskFlag("link-odd", "link", "The link looks unusual", "The address has words or a form that real senders rarely use."));
            if (floorBecause is null)
                floorBecause = links.Deceptive ? "its link hides where it goes" : "its link looks unusual";
        }

        // If checking a picture, send image bytes to the multimodal AI endpoint;
        // otherwise, send text only to save vision processing costs.
        var tip0 = new System.Runtime.CompilerServices.StrongBox<string?>();
        var (finalVerdict, finalReason, reviewStatus, failure) = await ApplyAiAsync(
            isPicture ? c => c.CheckScamAsync(null, request.Picture, imgType, ct) : c => c.CheckScamAsync(forAi, null, null, ct),
            aiRequired: isPicture, aiTip: tip0, floorVerdict, MockRiskRules.ReasonFor(floorVerdict, floorBecause), combinedFlags);
        if (!isPicture) (finalVerdict, finalReason) = NotSafeIfUnread(forAi, finalVerdict, finalReason);

        var flags = TopFlags(combinedFlags);
        var result = new CheckResult(NewId("chk"), request.Kind, finalVerdict, finalReason, flags,
            display, floor.Highlights.Where(h => flags.Any(f => f.Id == h.FlagId)).ToList(), null, reviewStatus, false, DateTimeOffset.Now, failure,
            Tip(finalVerdict, reviewStatus, tip0.Value, combinedFlags, finalReason));
        lock (_gate)
        {
            if (generation != _generation) return result;
            _checks[result.Id] = new(result, text.Trim(), reviewStatus == ReviewStatus.Held ? DateTimeOffset.Now : null);
            MarkVisitorItem(result.Id);
        }
        return result;
    }

    /// <summary>
    /// The one place the AI result meets the local floor. The AI can only make the verdict stricter.
    /// Any failure, exception or odd answer holds the item. When <paramref name="aiRequired"/> is set
    /// (pictures, which local rules cannot read) a missing checker also holds it.
    /// </summary>
    private async Task<(Verdict Verdict, string Reason, ReviewStatus Status, FailureInfo? Failure)> ApplyAiAsync(
        Func<IMetaScamChecker, Task<MetaScamAnalysis>> ask, bool aiRequired, System.Runtime.CompilerServices.StrongBox<string?> aiTip,
        Verdict floorVerdict, string floorReason, List<RiskFlag> flags)
    {
        var r = await ApplyAiCoreAsync(ask, aiRequired, aiTip, floorVerdict, floorReason, flags);

        // Someone may be in danger, or the checker wants a human: the helper is always told, whatever else was decided.
        // The local rules and the AI can both raise this. Nothing can lower it.
        if (r.Status != ReviewStatus.Held && flags.Any(f => f.Id is "at-risk" or "review-needed"))
            return (IsStricter(Verdict.BeCareful, r.Verdict) ? Verdict.BeCareful : r.Verdict, r.Reason, ReviewStatus.Held, r.Failure);
        return r;
    }

    private async Task<(Verdict Verdict, string Reason, ReviewStatus Status, FailureInfo? Failure)> ApplyAiCoreAsync(
        Func<IMetaScamChecker, Task<MetaScamAnalysis>> ask, bool aiRequired, System.Runtime.CompilerServices.StrongBox<string?> aiTip,
        Verdict floorVerdict, string floorReason, List<RiskFlag> flags)
    {
        // Fail closed for every kind of content: without the AI checker nothing is called checked, and it is held.
        if (metaChecker is null)
            return HoldForReview(flags, floorVerdict, "We could not check this, so your helper will look at it.", new("The checker is not switched on yet. Please tell the person who looks after this app.", "Code: NO_CHECKER\nWhat: No IMetaScamChecker is registered."));

        MetaScamAnalysis ai;
        try { ai = await ask(metaChecker); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return HoldForReview(flags, floorVerdict, "We could not finish checking this, so your helper will look at it.", new("Something went wrong while checking. Please try again in a minute.", $"Code: CHECK_EXCEPTION\nWhat: {ex.GetType().Name}\nTime: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"));
        }

        aiTip.Value = ai.WatchFor;
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

    // The AI only reads the first part of long text, so what it did not read can never be called safe.
    private static (Verdict, string) NotSafeIfUnread(string sentToAi, Verdict verdict, string reason) =>
        verdict == Verdict.LooksSafe && InputGuard.IsTooLong(sentToAi)
            ? (Verdict.BeCareful, "This is long, so I could only read the first part. Please take care with the rest.")
            : (verdict, reason);

    // Only a few flags are shown, and a sign that someone may need help is never one of those left out.
    private static List<RiskFlag> TopFlags(IEnumerable<RiskFlag> flags) =>
        flags.OrderBy(f => f.Id switch { "at-risk" => 0, "review-needed" => 1, _ => 2 }).Take(MaxFlagsShown).ToList();

    private static (Verdict, string, ReviewStatus, FailureInfo?) HoldForReview(List<RiskFlag> flags, Verdict floorVerdict, string reason, FailureInfo? failure)
    {
        if (flags.All(f => f.Id != "review-needed"))
            flags.Add(new("review-needed", "hourglass", "Needs helper check", "The automatic check could not finish, so your helper will check this."));
        return (IsStricter(Verdict.BeCareful, floorVerdict) ? Verdict.BeCareful : floorVerdict, reason, ReviewStatus.Held, failure);
    }

    // "Next time, watch for…": for anything that is not Looks safe, and anything held. Never empty, never on a clean result.
    private static string? Tip(Verdict verdict, ReviewStatus status, string? modelTip, IEnumerable<RiskFlag> flags, string reason) =>
        verdict == Verdict.LooksSafe && status != ReviewStatus.Held
            ? null
            : WatchFor.Choose(modelTip, flags.Select(f => f.Id), reason);

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
        var generation = CurrentGeneration;
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
            // The person's own lines are not scam signs, but what they say about their own safety always counts.
            var found = MockRiskRules.Scan(text);
            if (fromUser)
            {
                var atRisk = found.Flags.Where(f => f.Id == "at-risk").ToList();
                found = found with { Flags = atRisk, Highlights = found.Highlights.Where(h => h.FlagId == "at-risk").ToList(), Score = atRisk.Sum(f => MockRiskRules.Weight(f.Id)) };
            }
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

        var tip0 = new System.Runtime.CompilerServices.StrongBox<string?>();
        var forAi = Defang.ForHelper(conversation.Trim());
        var (finalVerdict, finalReason, reviewStatus, failure) = await ApplyAiAsync(
            c => c.CheckScamAsync(forAi, null, null, ct), aiRequired: false, aiTip: tip0, floorVerdict,
            MockRiskRules.ReasonFor(floorVerdict, MockRiskRules.BecauseFor(combinedFlags.FirstOrDefault()?.Id), startedFriendly: firstFlagged > 0),
            combinedFlags);
        (finalVerdict, finalReason) = NotSafeIfUnread(forAi, finalVerdict, finalReason);

        var flags = TopFlags(combinedFlags);
        var result = new CheckResult(NewId("chk"), CheckKind.Conversation, finalVerdict,
            finalReason,
            flags, display, [], thread, reviewStatus, false, DateTimeOffset.Now, failure,
            Tip(finalVerdict, reviewStatus, tip0.Value, combinedFlags, finalReason));
        lock (_gate)
        {
            if (generation != _generation) return result;
            _checks[result.Id] = new(result, conversation.Trim(), reviewStatus == ReviewStatus.Held ? DateTimeOffset.Now : null);
            MarkVisitorItem(result.Id);
        }
        return result;
    }

    public async Task<CheckResult?> GetCheckAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _checks.GetValueOrDefault(id)?.Result;
    }

    public async Task<CheckResult?> GetCheckForHelperAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _checks.TryGetValue(id, out var c) && CanSee(c) ? c.Result : null;
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
        RaiseChanged();
        return result;
    }

    public async Task DeleteCheckAsync(string checkId, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) _checks.Remove(checkId);
        RaiseChanged();
    }

    // ---------------- Show me ----------------

    public async Task<Guide?> GenerateGuideAsync(GuideRequest request, CancellationToken ct = default)
    {
        TellHelperIfAtRisk(request.Goal);
        await Wait(1600, ct);
        var generation = CurrentGeneration;
        if (string.IsNullOrWhiteSpace(request.Goal)) throw new BackstopApiException("Please say what you want to do.");

        string? photoType = null;
        if (request.Photo is not null)
        {
            if (request.Photo.Length > MaxModePhotoBytes) throw new BackstopApiException("That picture is too big. Please choose a smaller one.");
            photoType = ImageSniff.ImageType(request.Photo) ?? throw new BackstopApiException("We could not use that file. Please take a photo or choose a picture.");
        }

        // The AI writes the guide for any goal, looking at the photo when there is one.
        // An unsafe goal is refused (null). The photo goes to the AI and is never kept.
        if (guideWriter is null)
            throw new BackstopApiException("Show me is not switched on yet. Please tell the person who looks after this app.");
        var draft = await guideWriter.WriteAsync(request.Goal.Trim(), request.Photo, photoType, ct);
        if (!draft.Success)
            throw new BackstopApiException(draft.Failure?.Message ?? "Something went wrong. Please try again in a minute.");
        if (draft.Refused) return null;

        var guide = new Guide(NewId("g"), draft.Title, request.Goal.Trim(), draft.Picture, draft.Steps,
            draft.Care, DateTimeOffset.Now);
        lock (_gate)
        {
            if (generation != _generation) return guide;
            _guides[guide.Id] = new StoredGuide(guide, draft.Tags, draft.PastTense);
            MarkVisitorItem(guide.Id);
        }
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
            var help = new HelpRequest(NewId("h"), DateTimeOffset.Now, title, stepNumber, false);
            _help.Add(help);
            MarkVisitorItem(help.Id);
        }
        RaiseChanged();
    }

    // ---------------- Explain ----------------

    public async Task<Explanation> ExplainContentAsync(ExplainRequest request, CancellationToken ct = default)
    {
        await Wait(0, ct);
        var generation = CurrentGeneration;
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
            TellHelperIfAtRisk(typed);
        }

        if (documentExplainer is null)
            throw new BackstopApiException("Explain is not switched on yet. Please tell the person who looks after this app.");
        var reading = await documentExplainer.ExplainAsync(typed, request.File, mime, request.Level, request.Format, ct);
        if (!reading.Success)
            throw new BackstopApiException(reading.Failure?.Message ?? "Something went wrong. Please try again in a minute.");

        // A photo's words are only known now.
        if (typed is null) TellHelperIfAtRisk(reading.Text);

        var explanation = BuildExplanation(reading, typed ?? Defang.ForUser(reading.Text), request.Level, request.Format);
        if (typed is not null && InputGuard.IsTooLong(typed))
            explanation = explanation with { Chunks = [new("Only the first part", "This is long, so I only read the first part. Paste the rest on its own to read it too."), .. explanation.Chunks] };
        if (reading.NeedsHelper)
        {
            AskHelperToLook(explanation.Title);
            if (!explanation.Overview.Contains(Helper, StringComparison.OrdinalIgnoreCase))
                explanation = explanation with { Overview = $"{explanation.Overview} {Helper} can look at it with you." };
        }
        lock (_gate)
        {
            if (generation != _generation) return explanation;
            _explanations[explanation.Id] = explanation;
            MarkVisitorItem(explanation.Id);
        }
        return explanation;
    }

    public async Task<Explanation?> GetExplanationAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _explanations.GetValueOrDefault(id);
    }

    public async Task<ChatAnswer> AskAboutAsync(string explanationId, string question, CancellationToken ct = default)
    {
        await Wait(0, ct);
        if (string.IsNullOrWhiteSpace(question)) throw new BackstopApiException("Please type your question first.");
        if (question.Length > 500) throw new BackstopApiException("That is a bit long. Please make your question shorter.");
        Explanation? e;
        lock (_gate) e = _explanations.GetValueOrDefault(explanationId);
        if (e is null) throw new BackstopApiException("We could not find that page. Please explain it again.");
        TellHelperIfAtRisk(question);
        if (documentExplainer is null)
            throw new BackstopApiException("Questions are not switched on yet. Please tell the person who looks after this app.");

        var reply = await documentExplainer.AnswerAsync(e.OriginalText, Defang.ForUser(question.Trim()), ct);
        if (reply.NeedsHelper) AskHelperToLook(e.Title);
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
        if (best.Count == 0)
            return new MemoryAnswer(question.Trim(), "I could not find that in your memory yet. When you save a Show me or Explain page, you can ask about it here.", []);
        // The local match only shortlists cards. The AI writes the answer from those cards, and nothing else.
        if (memoryAnswerer is null)
            throw new BackstopApiException("Remember questions are not switched on yet. Please tell the person who looks after this app.");
        var draft = await memoryAnswerer.AnswerAsync(question.Trim(), best, ct);
        if (!draft.Success)
            throw new BackstopApiException(draft.Failure?.Message ?? "Something went wrong. Please try again in a minute.");
        if (!draft.Found)
            return new MemoryAnswer(question.Trim(), "I could not find that in your memory yet. When you save a Show me or Explain page, you can ask about it here.", []);
        var cards = draft.Uses.Where(i => i >= 0 && i < best.Count).Select(i => best[i]).ToList();
        if (cards.Count == 0) cards = best;
        return new MemoryAnswer(question.Trim(), draft.Answer, cards);
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
                g.Guide.Picture, g.Tags, steps,
                $"Last time you {g.PastTense} in {steps.Count} steps. The first one was: {LowerFirst(steps[0])}",
                MemorySource.ShowMe, false);
            AddMemory(guideId, card);
        }
        RaiseChanged();
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
            card = new MemoryCard(NewId("m"), e.Title, Today, e.Overview, "letter", ["letters"],
                e.Actions.Count > 0 ? e.Actions : e.Chunks.Select(c => c.Body).Take(4).ToList(),
                $"Last time, the {LowerFirst(e.Title)} said this: {e.Overview}", MemorySource.Explain, false);
            AddMemory(explanationId, card);
        }
        RaiseChanged();
        return card;
    }

    public async Task<MemoryCard> RenameMemoryAsync(string id, string title, CancellationToken ct = default)
    {
        await Wait(400, ct);
        if (string.IsNullOrWhiteSpace(title)) throw new BackstopApiException("Please type a name.");
        var card = Replace(id, c => c with { Title = title.Trim() });
        RaiseChanged();
        return card;
    }

    public async Task<MemoryCard> ShareMemoryAsync(string id, bool shared, CancellationToken ct = default)
    {
        await Wait(500, ct);
        var card = Replace(id, c => c with { Shared = shared });
        lock (_gate)
            Log(shared ? "share" : "minus", shared ? $"{User} shared \"{card.Title}\" with you." : $"{User} stopped sharing \"{card.Title}\".");
        RaiseChanged();
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
        RaiseChanged();
    }

    // ---------------- Trusted person ----------------

    public async Task<IReadOnlyList<ReviewItem>> GetReviewQueueAsync(CancellationToken ct = default)
    {
        await Wait(600, ct);
        var inbox = guardian.GetQueue().Select(FromMail);
        List<ReviewItem> checks;
        lock (_gate) checks = _checks.Values.Where(c => c.Result.Status == ReviewStatus.Held && CanSee(c)).Select(FromCheck).ToList();
        return inbox.Concat(checks).OrderByDescending(i => i.At).ToList();
    }

    public async Task<ReviewItem?> GetReviewItemAsync(string id, CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
        {
            if (_checks.TryGetValue(id, out var c)) return c.Result.Status == ReviewStatus.Held && CanSee(c) ? FromCheck(c) : null;
        }
        var m = guardian.Get(id);
        return m is { State: MessageState.Held } ? FromMail(m) : null;
    }

    public async Task ResolveReviewItemAsync(string id, ReviewDecision decision, CancellationToken ct = default)
    {
        await Wait(500, ct);
        var isCheck = false;
        lock (_gate)
        {
            if (_checks.TryGetValue(id, out var c))
            {
                isCheck = true;
                if (!CanSee(c)) throw new BackstopApiException($"{User} has not allowed you to see their checks.");
                // Only a held item can be decided, and Blocked is final.
                if (c.Result.Status != ReviewStatus.Held) throw new BackstopApiException("This has already been decided.");
                var r = decision switch
                {
                    ReviewDecision.Release => c.Result with { Status = ReviewStatus.Released, CallRequested = false },
                    ReviewDecision.Block => c.Result with { Status = ReviewStatus.Blocked, CallRequested = false },
                    _ => c.Result with { CallRequested = true },
                };
                _checks[id] = c with { Result = r };
                var title = TitleFor(c.Result, c.Original);
                Log(decision switch { ReviewDecision.Release => "check", ReviewDecision.Block => "cross", _ => "phone" },
                    decision switch
                    {
                        ReviewDecision.Release => $"You said {LowerFirst(title)} is safe.",
                        ReviewDecision.Block => $"You blocked {LowerFirst(title)}.",
                        _ => $"You will call {User} about {LowerFirst(title)}.",
                    });
            }
        }
        // Mail decisions are written to the activity log by the guardian service, and they raise its events, so they run outside the lock.
        if (!isCheck)
        {
            var m = guardian.Get(id);
            if (m is not { State: MessageState.Held }) throw new BackstopApiException("This has already been decided.");
            if (decision == ReviewDecision.Release && m.AwaitingScan) throw new BackstopApiException("The checker has not finished with this message yet.");
            switch (decision)
            {
                case ReviewDecision.Release: guardian.Approve(id); break;
                case ReviewDecision.Block: guardian.Block(id); break;
                default: guardian.CallFirst(id); break;
            }
        }
        RaiseChanged();
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

    internal const int MaxModePhotoBytes = 10 * 1024 * 1024;

    // ---------------- Demo support ----------------

    public void ResetDemo()
    {
        lock (_gate)
        {
            _generation++;
            _checks.Clear(); _stories.Clear(); _guides.Clear(); _explanations.Clear(); _photos.Clear(); _savedFrom.Clear();
            _visitorItems.Clear();
            _memories = MemoryFixtures.Seed();
            _activity = SeedActivity();
            _seededAt = DateTimeOffset.Now;
            _help = SeedHelp();
            ResetProfiles();
            ResetChat();
        }
        guardian.SetAccess(HelperPermissions.Default); // raises the store's event, so outside the lock
        RaiseChanged();
    }

    public void ClearMemories()
    {
        lock (_gate) { _memories.Clear(); _savedFrom.Clear(); }
        RaiseChanged();
    }

    // ---------------- Helpers ----------------

    // No artificial pause: the mock answers at once. The ms argument is kept so call sites need no change.
    private Task Wait(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (options.FailRequests) throw new BackstopApiException("We could not reach Backstop just now. Please try again in a moment.");
        return Task.CompletedTask;
    }

    private static string NewId(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    // Caller holds the lock.
    private void Log(string icon, string text)
    {
        _activity.Add(new ActivityEntry(DateTimeOffset.Now, icon, text));
        TrimActivity();
    }

    private MemoryCard? Existing(string sourceId) =>
        _savedFrom.TryGetValue(sourceId, out var memoryId) ? _memories.FirstOrDefault(c => c.Id == memoryId) : null;

    private void AddMemory(string sourceId, MemoryCard card)
    {
        _memories.Add(card);
        _savedFrom[sourceId] = card.Id;
        MarkVisitorItem(card.Id);
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

    private const string PictureNotKept = "The picture was not kept.";

    private ReviewItem FromCheck(StoredCheck c)
    {
        var r = c.Result;
        var signs = r.Flags.Count == 0 ? "We did not find clear warning signs." :
            $"We found {r.Flags.Count} warning sign{(r.Flags.Count == 1 ? "" : "s")}: {string.Join(", ", r.Flags.Select(f => LowerFirst(f.Title)))}.";
        var high = r.Thread?.Select((m, i) => (m.Level, Number: i + 1)).FirstOrDefault(x => x.Level == RiskLevel.High).Number ?? 0;
        var trend = high > 0 ? $" The risk rose across the chat: calm at first, high by message {high} of {r.Thread!.Count}." : "";
        var reasoning = $"{signs}{trend} {User} asked you to look before replying.";
        // Pictures are never kept, so the helper is told that rather than shown an empty box.
        var preview = r.Kind == CheckKind.Picture
            ? c.Original.Length == 0 ? PictureNotKept : $"{PictureNotKept}\n\n{Defang.ForHelper(c.Original)}"
            : Defang.ForHelper(c.Original);
        return new ReviewItem(r.Id, ReviewSource.Check, TitleFor(r, c.Original), preview, r.Verdict, r.Flags,
            reasoning, r.CallRequested, c.AskedAt ?? r.CheckedAt, r.WatchFor);
    }

    private static ReviewItem FromMail(MailMessage m) => new(
        m.Id, ReviewSource.Inbox, Defang.ForHelper(m.Subject), Defang.ForHelper(m.Body),
        m.Concern == Concern.Strong ? Verdict.DontReply : Verdict.BeCareful,
        m.Flags.Select(f => new RiskFlag(f.Title, f.Icon, f.Title, f.Explanation)).ToList(),
        m.Summary, m.CallRequested, m.ReceivedAt);

    // The same "stuck" request the seeded activity mentions, so it can be answered.
    private static List<HelpRequest> SeedHelp() => [new HelpRequest("h-seed", DateTimeOffset.Now.AddHours(-5), "Setting the washing machine", 4, false)];

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
