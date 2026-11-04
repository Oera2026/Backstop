namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// Example goals offered on the empty Show me screen. They are only starting prompts:
/// the AI writes a fresh guide for whichever one the person picks.
/// </summary>
public static class GuideFixtures
{
    public static IReadOnlyList<string> Suggestions { get; } = ["Make the bed", "Fill in a form", "Set the washing machine"];
}
