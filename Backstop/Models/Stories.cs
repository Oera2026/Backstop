namespace Backstop.Models;

// Request and response shapes for Stories and "Make sense of it". See API_CONTRACT.md for the endpoints.

/// <summary>Ready can be read. Held waits for the trusted person; the user sees no words until it is released.</summary>
public sealed record StoryRequest(string Situation, string? When);

/// <summary>One page of a story: a short heading, a few short sentences and a picture key.</summary>
public sealed record StoryPage(string Heading, string Text, string Picture, Scene? Scene = null);

/// <summary>
/// A painted scene: a setting, the people in it and a few things. Every field is a key from <c>SceneVocabulary</c>,
/// so the model can only choose from fixed, calm pictures. It never sends markup, and the app does the layout.
/// Stories saved by an older version have no cast and fall back to their picture key.
/// </summary>
public sealed record Scene(string? Setting = null, string? Light = null, IReadOnlyList<Actor>? Cast = null, IReadOnlyList<string>? Things = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDrawable => !string.IsNullOrEmpty(Setting) && Cast is { Count: > 0 };
}

/// <summary>One person. Role sets the clothes. Skin is 1-4, HairColour 1-5, Colour 1-6. Age, Hair, Pose and Mood are keys.</summary>
public sealed record Actor(string Role, string Age, int Skin, string Hair, int HairColour, int Colour, bool Glasses, string Pose, string Mood, string? Holding);

public sealed record Story(
    string Id,
    string Title,
    string? When,
    IReadOnlyList<StoryPage> Pages,
    DateTimeOffset MadeAt);

