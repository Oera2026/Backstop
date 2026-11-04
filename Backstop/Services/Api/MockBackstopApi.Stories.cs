using Backstop.Models;
using Backstop.Services.Api.Fixtures;

namespace Backstop.Services.Api;

// Stories are written by the AI (see MetaStoryWriter) and checked before they are shown.
// Without the writer there is no story: nothing canned is ever passed off as its work.
public sealed partial class MockBackstopApi
{
    private readonly Dictionary<string, Story> _stories = [];

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

        var story = new Story(NewId("s"), title, when, pages, DateTimeOffset.Now);
        lock (_gate)
        {
            _stories[story.Id] = story;
            MarkVisitorItem(story.Id);
            if (notify) Log("story", $"{User} made a story about \"{Shorten(text)}\". It may be a sensitive topic, so you may want to ask how they are.");
        }
        Changed?.Invoke();
        return story;
    }

    public async Task<Story?> GetStoryAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) return _stories.GetValueOrDefault(id);
    }

    public async Task<IReadOnlyList<Story>> GetStoriesAsync(CancellationToken ct = default)
    {
        await Wait(400, ct);
        lock (_gate)
            return _stories.Values.OrderByDescending(s => s.MadeAt).ToList();
    }

    public async Task DeleteStoryAsync(string id, CancellationToken ct = default)
    {
        await Wait(300, ct);
        lock (_gate) _stories.Remove(id);
        Changed?.Invoke();
    }

    private string Fill(string s) => s.Replace("{helper}", Helper);
}
