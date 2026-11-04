namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// Topics the carer should hear about. This list is a floor, not a writer: the AI writes every story,
/// and a story about one of these words tells the carer, whatever the AI said. It never stops the story.
/// </summary>
public static class StoryFixtures
{
    public static readonly string[] SensitiveWords =
        ["money", "bank", "loan", "debt", "stranger", "police", "court", "medicine", "tablets"];

    public static bool IsSensitive(string text) =>
        SensitiveWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
