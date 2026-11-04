namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// Topics a trusted person should see first. This list is a floor, not a writer: the AI writes every story,
/// and a story about one of these words is held until the helper has read it, whatever the AI said.
/// </summary>
public static class StoryFixtures
{
    public static readonly string[] SensitiveWords =
        ["money", "bank", "loan", "debt", "stranger", "argu", "shout", "angry", "hurt", "tablet", "medicine", "police", "court"];

    public static bool IsSensitive(string text) =>
        SensitiveWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
