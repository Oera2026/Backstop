using Backstop.Models;

namespace Backstop.Services.Api;

/// <summary>
/// Everything the Check, Show me, Explain, Remember and trusted-person screens need.
/// The screens talk only to this interface. A real back end replaces <see cref="MockBackstopApi"/>;
/// nothing else changes. Endpoints and payloads are listed in API_CONTRACT.md.
/// Every call can fail: it then throws <see cref="BackstopApiException"/> and the screen shows its error state.
/// </summary>
public interface IBackstopApi
{
    /// <summary>Raised after any change, so open screens (for example the trusted person's queue) update live.</summary>
    event Action? Changed;

    Household Household { get; }

    // --- Check ---
    Task<CheckResult> CheckContentAsync(CheckRequest request, CancellationToken ct = default);
    Task<CheckResult> GetConversationAnalysisAsync(string conversation, CancellationToken ct = default);
    Task<CheckResult?> GetCheckAsync(string id, CancellationToken ct = default);
    /// <summary>Checks the user has sent to their trusted person, newest first.</summary>
    Task<IReadOnlyList<CheckResult>> GetAskedChecksAsync(CancellationToken ct = default);
    Task<CheckResult> AskTrustedPersonAsync(string checkId, CancellationToken ct = default);
    Task DeleteCheckAsync(string checkId, CancellationToken ct = default);

    // --- Show me ---
    /// <summary>Returns null when there is no guide for that goal yet.</summary>
    Task<Guide?> GenerateGuideAsync(GuideRequest request, CancellationToken ct = default);
    Task<Guide?> GetGuideAsync(string id, CancellationToken ct = default);
    /// <summary>"I'm stuck": tells the trusted person which step the user is on.</summary>
    Task AskForHelpAsync(string guideId, int stepNumber, CancellationToken ct = default);

    // --- Explain ---
    Task<Explanation> ExplainContentAsync(ExplainRequest request, CancellationToken ct = default);
    Task<Explanation?> GetExplanationAsync(string id, CancellationToken ct = default);
    Task<ChatAnswer> AskAboutAsync(string explanationId, string question, CancellationToken ct = default);

    // --- Remember ---
    Task<IReadOnlyList<MemoryCard>> GetMemoryCardsAsync(string? search = null, CancellationToken ct = default);
    Task<MemoryCard?> GetMemoryCardAsync(string id, CancellationToken ct = default);
    Task<MemoryAnswer> AskMemoryAsync(string question, CancellationToken ct = default);
    IReadOnlyList<string> SuggestedMemoryQuestions { get; }
    Task<MemoryCard> SaveGuideToMemoryAsync(string guideId, CancellationToken ct = default);
    Task<MemoryCard> SaveExplanationToMemoryAsync(string explanationId, CancellationToken ct = default);
    Task<MemoryCard> RenameMemoryAsync(string id, string title, CancellationToken ct = default);
    Task<MemoryCard> ShareMemoryAsync(string id, bool shared, CancellationToken ct = default);
    Task DeleteMemoryAsync(string id, CancellationToken ct = default);

    // --- Stories ---
    /// <summary>
    /// Writes a story about something coming up. Sensitive topics come back <see cref="StoryStatus.Held"/>
    /// with no pages until the trusted person decides. Fail closed: anything unclear is held.
    /// </summary>
    Task<Story> MakeStoryAsync(StoryRequest request, CancellationToken ct = default);
    /// <summary>Returns null when the story does not exist or was blocked. A held story has no pages.</summary>
    Task<Story?> GetStoryAsync(string id, CancellationToken ct = default);
    /// <summary>The user's stories, newest first. Blocked stories are left out.</summary>
    Task<IReadOnlyList<Story>> GetStoriesAsync(CancellationToken ct = default);
    Task DeleteStoryAsync(string id, CancellationToken ct = default);
    /// <summary>"Make sense of it": reads what happened and gives more than one way to see it.</summary>
    Task<SituationExplanation> MakeSenseOfAsync(string whatHappened, CancellationToken ct = default);

    // --- Trusted person ---
    /// <summary>Held items from the inbox and from Check, newest first.</summary>
    Task<IReadOnlyList<ReviewItem>> GetReviewQueueAsync(CancellationToken ct = default);
    Task<ReviewItem?> GetReviewItemAsync(string id, CancellationToken ct = default);
    Task ResolveReviewItemAsync(string id, ReviewDecision decision, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MemoryCard>> GetSharedMemoriesAsync(CancellationToken ct = default);

    /// <summary>A photo the user added (Show me, or a memory card). Served by /mode-photo/{id}.</summary>
    ContactPhoto? GetPhoto(string id);

    // --- Demo support ---
    void ResetDemo();
    /// <summary>Empties Remember so its empty state can be shown.</summary>
    void ClearMemories();
}

/// <summary>A call failed. <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class BackstopApiException(string message) : Exception(message);
