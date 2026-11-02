using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>What the writer sends back. Pages are already checked; Sensitive can only make the story stricter (held).</summary>
public sealed record StoryDraft(bool Success, string Title, IReadOnlyList<StoryPage> Pages, bool Sensitive, FailureInfo? Failure = null);

/// <summary>Writes a social story and its animated scenes. The real one is <see cref="MetaStoryWriter"/>.</summary>
public interface IStoryWriter
{
    Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default);
}

/// <summary>The only shapes, tones and motions a scene may use. Anything else is dropped.</summary>
public static class SceneVocabulary
{
    public static readonly HashSet<string> Shapes =
        ["person", "door", "bus", "chair", "clock", "building", "shop", "speech", "heart", "sun", "tree", "table", "paper"];
    public static readonly HashSet<string> Tones = ["band", "paper", "mark", "ink"];
    public static readonly HashSet<string> Motions = ["none", "float", "pulse", "slide-in", "fade-in"];
    public static readonly HashSet<string> Pictures = ["door", "bus", "desk", "chair", "talk", "feel", "calm", "shop", "home"];
}
