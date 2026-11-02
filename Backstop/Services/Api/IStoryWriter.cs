using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>What the writer sends back. Pages are already checked; Sensitive can only make the story stricter (held).</summary>
public sealed record StoryDraft(bool Success, string Title, IReadOnlyList<StoryPage> Pages, bool Sensitive, FailureInfo? Failure = null);

/// <summary>Writes a social story and its animated scenes. The real one is <see cref="MetaStoryWriter"/>.</summary>
public interface IStoryWriter
{
    Task<StoryDraft> WriteAsync(string situation, string? when, CancellationToken ct = default);
}

/// <summary>The only settings, people and things a scene may use. Anything else is dropped. Nothing here can show danger or harm.</summary>
public static class SceneVocabulary
{
    public static readonly HashSet<string> Settings = ["clinic", "bus_stop", "shop", "home", "kitchen", "park", "office", "street"];
    public static readonly HashSet<string> Lights = ["day", "evening"];
    public static readonly HashSet<string> Roles = ["me", "helper", "friend", "neighbour", "family", "doctor", "nurse", "driver", "shopkeeper", "receptionist", "teacher"];
    public static readonly HashSet<string> Ages = ["child", "adult", "older"];
    public static readonly HashSet<string> Hairs = ["short", "long", "curly", "bun", "bald"];
    public static readonly HashSet<string> Poses = ["stand", "wave", "talk", "walk", "sit"];
    public static readonly HashSet<string> Moods = ["calm", "happy", "unsure", "thinking"];
    public static readonly HashSet<string> Holdings = ["paper", "phone", "cup", "bag"];
    public static readonly HashSet<string> Things = ["bus", "door", "clock", "plant", "chair", "table"];
    public static readonly HashSet<string> Pictures = ["door", "bus", "desk", "chair", "talk", "feel", "calm", "shop", "home"];
}
