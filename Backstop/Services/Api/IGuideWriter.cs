using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>What the writer sends back. Steps are already checked; Refused means the goal was not safe to guide.</summary>
public sealed record GuideDraft(
    bool Success,
    bool Refused,
    string Title,
    IReadOnlyList<string> Steps,
    CareNote? Care,
    string Picture,
    IReadOnlyList<string> Tags,
    string PastTense,
    FailureInfo? Failure = null);

/// <summary>Writes a step-by-step guide for a goal, looking at the person's photo when they add one. The real one is <see cref="MetaGuideWriter"/>.</summary>
public interface IGuideWriter
{
    Task<GuideDraft> WriteAsync(string goal, byte[]? photo, string? mimeType, CancellationToken ct = default);
}

/// <summary>The only "what it looks like when done" pictures a guide may use. The model picks one; the app draws it.</summary>
public static class GuidePictures
{
    public static readonly HashSet<string> Keys = ["washer", "bed", "form", "router", "letter", "steps"];
    public const string Fallback = "steps";
}
