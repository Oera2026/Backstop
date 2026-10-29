namespace Backstop.Models;

// Request and response shapes for Stories and "Make sense of it". See API_CONTRACT.md for the endpoints.

/// <summary>Ready can be read. Held waits for the trusted person; the user sees no words until it is released.</summary>
public enum StoryStatus { Ready, Held, Blocked }

public sealed record StoryRequest(string Situation, string? When);

/// <summary>One page of a story: a short heading, a few short sentences and a picture key.</summary>
public sealed record StoryPage(string Heading, string Text, string Picture);

public sealed record Story(
    string Id,
    string Title,
    string? When,
    // Empty while the story is held: the user sees no words until the trusted person releases it.
    IReadOnlyList<StoryPage> Pages,
    StoryStatus Status,
    DateTimeOffset MadeAt);

/// <summary>One way to read what happened. Likelihood is a word ("Often", "Sometimes"), never a percentage.</summary>
public sealed record Possibility(string Likelihood, string Text);

public sealed record SituationExplanation(
    string Id,
    string WhatHappened,
    string Summary,
    IReadOnlyList<Possibility> Possibilities,
    IReadOnlyList<string> WhatYouCanDo,
    IReadOnlyList<string> YouCouldSay,
    // Shown when the situation sounds upsetting: points the user to their trusted person.
    string? TalkToSomeone);
