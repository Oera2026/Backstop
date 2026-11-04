namespace Backstop.Models;

// Request and response shapes for the Check, Show me, Explain, Remember and trusted-person modes.
// A real back end keeps these shapes; see API_CONTRACT.md for the endpoints they map to.

/// <summary>The user and the person who looks after them. MOCK: synthetic names.</summary>
public sealed record Household(string UserName, string TrustedPersonName, string TrustedPersonRelationship);

// ---------- Check ----------

public enum CheckKind { Message, Conversation, Picture }

/// <summary>Looks safe (green), Be careful (amber) or Don't reply (red). Always shown with an icon and a word.</summary>
public enum Verdict { LooksSafe, BeCareful, DontReply }

/// <summary>Where a checked item is in the trusted person's hands.</summary>
public enum ReviewStatus { NotAsked, Held, Released, Blocked }

/// <summary>One thing that looked worrying, in plain words for the user.</summary>
public sealed record RiskFlag(string Id, string Icon, string Title, string Explanation);

/// <summary>A run of characters in DisplayText that matched a flag.</summary>
public sealed record Highlight(int Start, int Length, string FlagId);

public sealed record CheckRequest(CheckKind Kind, string? Text, byte[]? Picture = null, string? PictureName = null);

/// <summary>Risk level of one message in a thread. Shown with a word and a bar, never colour alone.</summary>
public enum RiskLevel { Calm, Rising, High }

public sealed record ThreadMessage(
    string Sender,
    bool FromUser,
    string Text,
    int Risk,
    RiskLevel Level,
    IReadOnlyList<string> FlagIds,
    IReadOnlyList<Highlight> Highlights);

public sealed record CheckResult(
    string Id,
    CheckKind Kind,
    Verdict Verdict,
    string Reason,
    IReadOnlyList<RiskFlag> Flags,
    // The text as the user sees it: links already removed.
    string DisplayText,
    IReadOnlyList<Highlight> Highlights,
    // Only for conversations: each message with a risk that builds across the thread.
    IReadOnlyList<ThreadMessage>? Thread,
    ReviewStatus Status,
    bool CallRequested,
    DateTimeOffset CheckedAt,
    // Set when the automatic check could not finish: plain words plus technical detail.
    FailureInfo? Failure = null,
    // "Next time, watch for…": one teaching sentence when the result is not Looks safe or is held. Null otherwise.
    string? WatchFor = null);

// ---------- Show me ----------

/// <summary>A box on the user's photo, in percent of its width and height.</summary>

/// <summary>Shown on tasks that need care (medicines, electrics, chemicals).</summary>
public sealed record CareNote(string Topic, string Text);

public sealed record GuideRequest(string Goal, byte[]? Photo, string? PhotoName);

public sealed record Guide(
    string Id,
    string Title,
    string Goal,
    // Key of the "what it looks like when done" guide picture.
    string Picture,
    IReadOnlyList<string> Steps,
    CareNote? NeedsCare,
    DateTimeOffset MadeAt);

// ---------- Explain ----------

public enum ReadingLevel { VerySimple, Simple, Standard }
public enum ExplainFormat { Summary, Steps, Questions }

public sealed record ExplainRequest(string? Text, byte[]? File, string? FileName, ReadingLevel Level, ExplainFormat Format);

public sealed record ExplainChunk(string Heading, string Body);

/// <summary>A hard word in OriginalText and what it means.</summary>
public sealed record HardWord(int Start, int Length, string Meaning);

public sealed record Explanation(
    string Id,
    string Title,
    // "What this is and what you need to do".
    string Overview,
    string? Deadline,
    IReadOnlyList<ExplainChunk> Chunks,
    IReadOnlyList<string> Actions,
    string OriginalText,
    IReadOnlyList<HardWord> HardWords,
    ReadingLevel Level,
    ExplainFormat Format);

public sealed record ChatAnswer(string Question, string Answer);

// ---------- Remember ----------

public enum MemorySource { ShowMe, Explain }

public sealed record MemoryCard(
    string Id,
    string Title,
    DateOnly Date,
    string Summary,
    string Picture,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Steps,
    // How the answer starts when this card is found, for example "Last time you reset the internet box by…".
    string HowYouDidIt,
    MemorySource Source,
    bool Shared);

public sealed record MemoryAnswer(string Question, string Answer, IReadOnlyList<MemoryCard> Cards);

// ---------- Trusted person ----------

public enum ReviewSource { Inbox, Check, Story }
public enum ReviewDecision { Release, Block, CallFirst }

public sealed record ReviewItem(
    string Id,
    ReviewSource Source,
    string Title,
    // Text only, links defanged (hxxp://example[.]com).
    string Preview,
    Verdict Verdict,
    IReadOnlyList<RiskFlag> Flags,
    string Reasoning,
    bool CallRequested,
    DateTimeOffset At,
    string? WatchFor = null);

public sealed record ActivityEntry(DateTimeOffset At, string Icon, string Text);
