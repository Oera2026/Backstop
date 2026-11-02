using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

// MOCK: how Stories are "written". A real back end sends the situation to a language
// model and checks the answer before it is shown. Here each situation is matched by its words.
public sealed partial class MockBackstopApi
{
    private sealed record StoredStory(Story Story, string Situation);

    private readonly Dictionary<string, StoredStory> _stories = [];

    public async Task<Story> MakeStoryAsync(StoryRequest request, CancellationToken ct = default)
    {
        if (storyWriter is null) await Wait(1600, ct);
        if (string.IsNullOrWhiteSpace(request.Situation)) throw new BackstopApiException("Please say what is coming up.");

        var text = request.Situation.Trim();
        var when = string.IsNullOrWhiteSpace(request.When) ? null : request.When.Trim();

        string title;
        List<StoryPage> pages;
        // Stories written by the AI are shown straight away (owner decision, 2 Nov 2026). Only the offline sample stories keep the hold.
        // A story the AI could not write is never invented: there is simply no story.
        var held = StoryFixtures.IsSensitive(text);
        if (storyWriter is not null)
        {
            // If the AI cannot write it, there is no story. Nothing canned is passed off as its work.
            var draft = await storyWriter.WriteAsync(text, when, ct);
            if (!draft.Success) throw new BackstopApiException("I could not make your story. Please try again.");
            title = draft.Title;
            pages = draft.Pages.Select(p => p with { Text = Fill(p.Text) }).ToList();
            held = false;
        }
        else
        {
            var template = StoryFixtures.Match(text);
            title = template.Title;
            pages = template.Pages.Select(p => p with { Text = Fill(p.Text) }).ToList();
        }

        var story = new Story(NewId("s"), title, when, pages, held ? StoryStatus.Held : StoryStatus.Ready, DateTimeOffset.Now);
        lock (_gate)
        {
            _stories[story.Id] = new(story, text);
            if (held) Log("story", $"{User} made a story about \"{Shorten(text)}\". It is waiting for you.");
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
