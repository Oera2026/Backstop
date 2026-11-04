using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>
/// Spots a message that asks for a story ("tell me a story about the dentist", "can you make a social story for the bus").
/// Those always become a visual social story, never a text answer. Plain mentions of the word (a story in the news, "my stories")
/// do not count, and a long pasted message is never treated as a request.
/// </summary>
public static partial class StoryIntent
{
    private const int MaxLength = 300;

    [GeneratedRegex(@"\bsocial\s+stor(y|ies)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Social();

    [GeneratedRegex(@"\b(make|write|tell|create|give|need|want|do|prepare|build)\b[^.?!]{0,40}\b(a|an|visual|picture|short|little)\s+(\w+\s+){0,2}story\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Asked();

    [GeneratedRegex(@"^\W*(a |an |the )?(visual |picture |short )?story\s+(about|for|on|of)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Starts();

    public static bool Wants(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;
        return Social().IsMatch(text) || Asked().IsMatch(text) || Starts().IsMatch(text);
    }
}
