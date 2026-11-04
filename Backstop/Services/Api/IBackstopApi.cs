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
    /// <summary>The same check for the helper's review page. Null when the helper's access does not cover it.</summary>
    Task<CheckResult?> GetCheckForHelperAsync(string id, CancellationToken ct = default);
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

    // --- Ask Me ---
    /// <summary>A real conversation with the AI. The helper is told whenever someone may need help.</summary>
    Task<AskReply> AskAsync(IReadOnlyList<AskTurn> history, string message, AskContext? context = null, CancellationToken ct = default);

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
    /// Writes a story about something coming up. A sensitive topic still gets its story, and the trusted person is told.
    /// </summary>
    Task<Story> MakeStoryAsync(StoryRequest request, CancellationToken ct = default);
    /// <summary>Returns null when the story does not exist.</summary>
    Task<Story?> GetStoryAsync(string id, CancellationToken ct = default);
    /// <summary>The user's stories, newest first.</summary>
    Task<IReadOnlyList<Story>> GetStoriesAsync(CancellationToken ct = default);
    Task DeleteStoryAsync(string id, CancellationToken ct = default);

    // --- Chat ---
    /// <summary>The people the user may message. Set by the helper; the user cannot add to it.</summary>
    IReadOnlyList<ChatPerson> ChatPeople { get; }
    /// <summary>One thread per approved person, newest first. Held messages are never part of it.</summary>
    Task<IReadOnlyList<ChatThread>> GetChatThreadsAsync(CancellationToken ct = default);
    /// <summary>Delivered messages only, oldest first. Held and blocked messages are never returned.</summary>
    Task<IReadOnlyList<ChatMessage>> GetChatMessagesAsync(string personId, CancellationToken ct = default);
    /// <summary>All the user is told about held messages: how many. No sender, no words.</summary>
    Task<int> GetHeldChatCountAsync(CancellationToken ct = default);
    Task<ChatMessage> SendChatAsync(string personId, string text, CancellationToken ct = default);

    // --- Chat, helper side ---
    /// <summary>Messages from other users that failed a check. Always visible to the helper.</summary>
    Task<IReadOnlyList<HeldChat>> GetHeldChatsAsync(CancellationToken ct = default);
    /// <summary>Release delivers the message, Block ends it. Anything else leaves it held (fail closed).</summary>
    Task ResolveHeldChatAsync(string id, ReviewDecision decision, CancellationToken ct = default);
    /// <summary>The helper writes to the user. Shows in the user's chat with the household's trusted person.</summary>
    Task<ChatMessage> SendChatAsHelperAsync(string text, CancellationToken ct = default);
    /// <summary>A normal chat between the user and someone else. Throws unless the helper is allowed to see chats.</summary>
    Task<IReadOnlyList<ChatMessage>> GetChatLogAsync(string personId, CancellationToken ct = default);
    /// <summary>What the user allowed this helper to do. Changes how much of the chats the helper can read.</summary>
    HelperPermissions HelperAccess { get; }
    void SetHelperAccess(HelperPermissions access);

    // --- Profile: your own picture and whether you show as online ---
    Profile GetProfile(ProfileRole role);
    /// <summary>Hidden looks like offline to the other side.</summary>
    void SetPresence(ProfileRole role, Presence presence);
    /// <summary>Returns false (and saves nothing) unless the bytes really are a PNG, JPEG, WebP or GIF under 3 MB.</summary>
    bool SetProfilePhoto(ProfileRole role, byte[] bytes);
    void RemoveProfilePhoto(ProfileRole role);

    // --- Helper: everything waiting, history, and who the user can message ---
    /// <summary>Everything that needs the helper, worst first, already limited to what this helper may see.</summary>
    HelperDigest GetHelperDigest();
    /// <summary>Finished decisions on mail, Check, stories and chats, newest first.</summary>
    IReadOnlyList<HistoryEntry> GetDecisionHistory();
    /// <summary>The helper helped with a "stuck" request.</summary>
    void ResolveHelpRequest(string id);
    /// <summary>The helper made the call they promised. The item stays held until they decide.</summary>
    void MarkCalled(string id);
    /// <summary>Puts a person on the list the user may message. Throws a plain-language error when the details are not valid.</summary>
    ChatPerson AddChatPerson(string name, string relationship);
    void RemoveChatPerson(string id);

    // --- Trusted person ---
    /// <summary>Held items from the inbox and from Check, newest first.</summary>
    Task<IReadOnlyList<ReviewItem>> GetReviewQueueAsync(CancellationToken ct = default);
    Task<ReviewItem?> GetReviewItemAsync(string id, CancellationToken ct = default);
    Task ResolveReviewItemAsync(string id, ReviewDecision decision, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MemoryCard>> GetSharedMemoriesAsync(CancellationToken ct = default);

    /// <summary>A profile picture. Served by /mode-photo/{id}.</summary>
    ContactPhoto? GetPhoto(string id);

    /// <summary>Deletes what visitors sent to Meta AI once its time is up. An open safeguarding alert stays, without their words. Returns how many items went.</summary>
    int DisposeExpiredVisitorData();

    // --- Demo support ---
    void ResetDemo();
    /// <summary>Empties Remember so its empty state can be shown.</summary>
    void ClearMemories();
}

/// <summary>A call failed. <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class BackstopApiException(string message) : Exception(message);
