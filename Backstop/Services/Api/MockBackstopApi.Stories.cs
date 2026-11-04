using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

// Stories are written by the AI (see MetaStoryWriter) and checked before they are shown.
// Without the writer there is no story: nothing canned is ever passed off as its work.
public sealed partial class MockBackstopApi
{
    private sealed record StoredStory(Story Story, string Situation);

    private readonly Dictionary<string, StoredStory> _stories = [];

    public async Task<Story> MakeStoryAsync(StoryRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Situation)) throw new BackstopApiException("Please say what is coming up.");

        var text = request.Situation.Trim();
        var when = string.IsNullOrWhiteSpace(request.When) ? null : request.When.Trim();

        if (storyWriter is null)
            throw new BackstopApiException("Stories are not switched on yet. Please tell the person who looks after this app.");
        var draft = await storyWriter.WriteAsync(text, when, ct);
        if (!draft.Success) throw new BackstopApiException("I could not make your story. Please try again.");
        var title = draft.Title;
        var pages = draft.Pages.Select(p => p with { Text = Fill(p.Text) }).ToList();

        // A sensitive topic never blocks the story. The carer is told, and the person gets their story straight away.
        // The word list is a floor: the AI cannot lower it.
        var notify = draft.Sensitive || StoryFixtures.IsSensitive(text);

        var story = new Story(NewId("s"), title, when, pages, StoryStatus.Ready, DateTimeOffset.Now);
        lock (_gate)
        {
            _stories[story.Id] = new(story, text);
            MarkVisitorItem(story.Id);
            if (notify) Log("story", $"{User} made a story about \"{Shorten(text)}\". It may be a sensitive topic, so you may want to ask how they are.");
        }
        Changed?.Invoke();
        return ForUser(story);
    }

    public async Task<Story?> GetStoryAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _stories.TryGetValue(id, out var s) ? ForUserOrNull(s.Story) : null;
    }

    public async Task<IReadOnlyList<Story>> GetStoriesAsync(CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
            return _stories.Values.Select(s => ForUserOrNull(s.Story)).OfType<Story>().OrderByDescending(s => s.MadeAt).ToList();
    }

    public async Task DeleteStoryAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) _stories.Remove(id);
        Changed?.Invoke();
    }


    // A held story shows the user no words (the same rule as held mail). A blocked story is invisible.
    private static Story ForUser(Story s) => s.Status == StoryStatus.Held ? s with { Title = "Your story", When = null, Pages = [] } : s;
    private static Story? ForUserOrNull(Story s) => s.Status == StoryStatus.Blocked ? null : ForUser(s);

    private string Fill(string s) => s.Replace("{helper}", Helper);

    private ReviewItem FromStory(StoredStory s)
    {
        var story = s.Story;
        var preview = string.Join("\n\n", story.Pages.Select(p => $"{p.Heading}\n{p.Text}"));
        return new ReviewItem(story.Id, ReviewSource.Story, $"A story: {story.Title}", preview, Verdict.BeCareful,
            [new RiskFlag("sensitive", "alert", "Sensitive topic", "The story is about something that may need a person to check it first.")],
            $"{User} asked for a story about \"{Shorten(s.Situation)}\". It touches on a sensitive topic, so it is held until you have read it.",
            false, story.MadeAt);
    }
}
