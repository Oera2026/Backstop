# Backstop modes – API contract

The Check, Show me, Explain, Remember and trusted-person screens talk to one interface,
`IBackstopApi` (`Backstop/Services/Api/IBackstopApi.cs`). Today it is served by
`MockBackstopApi`, which keeps everything in memory and waits 300–1600 ms per call.

To wire a real back end, write a class that implements `IBackstopApi` by calling the
endpoints below, and register it in `Program.cs` in place of `MockBackstopApi`. No screen changes.

All payload shapes are the C# records in `Backstop/Models/Modes.cs`. JSON uses camelCase and
string enums. Dates are ISO 8601 (`DateTimeOffset`), and `DateOnly` is sent as `yyyy-MM-dd`.

These are assumptions for the mock. Change them to match the real service.

## Conventions

- **Auth:** each request carries the signed-in person. The user and the trusted person are different
  accounts. Trusted-person endpoints must refuse a user token. The mock has no auth.
- **Errors:** any non-2xx response becomes a `BackstopApiException`, whose message is shown to the user.
  Send `{ "message": "plain words for the user" }` with the error.
- **Not found:** `GET` by id returns `404`, which the client maps to `null`.
- **Uploads:** pictures and PDFs are `multipart/form-data`. The server decides the file type from the
  bytes, never from the name. It accepts PNG, JPEG, GIF and WebP (plus PDF for Explain), up to 10 MB.
- **Live updates:** `IBackstopApi.Changed` fires after any change. A real client raises it from a
  server push (for example SignalR or Server-Sent Events) on `/events`.
- **Fail closed:** anything sent to the trusted person stays `Held` until they decide. `Blocked` is final.

## Household

| Method | Path | Response |
|---|---|---|
| GET | `/household` | `Household { userName, trustedPersonName, trustedPersonRelationship }` |

## Check

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `CheckContentAsync` | POST | `/checks` | `CheckRequest { kind: Message\|Picture, text?, picture? (file), pictureName? }` | `CheckResult` |
| `GetConversationAnalysisAsync` | POST | `/checks/conversation` | `{ conversation: string }` (one message per line, `Name: text`) | `CheckResult` with `thread` |
| `GetCheckAsync` | GET | `/checks/{id}` | – | `CheckResult` or 404 |
| `GetAskedChecksAsync` | GET | `/checks?asked=true` | – | `CheckResult[]`, newest first |
| `AskTrustedPersonAsync` | POST | `/checks/{id}/ask` | – | `CheckResult` (status `Held`) |
| `DeleteCheckAsync` | DELETE | `/checks/{id}` | – | 204 |

`CheckResult`:

```
{
  id, kind: Message|Conversation|Picture,
  verdict: LooksSafe|BeCareful|DontReply,
  reason: "Please don't reply yet, because it asks for money or gift cards.",
  flags: [ { id, icon, title, explanation } ],        // 0–4, most serious first
  displayText: string,                                   // links already removed
  highlights: [ { start, length, flagId } ],             // offsets into displayText
  thread: [ { sender, fromUser, text, risk: 0-100, level: Calm|Rising|High,
              flagIds: [], highlights: [] } ] | null,     // conversations only
  status: NotAsked|Held|Released|Blocked,
  callRequested: bool,
  checkedAt
}
```

Flag ids used by the mock: `money`, `private`, `secret`, `new-number`, `other-app`, `hurry`, `feelings`, `link`.
Icons are names from `Components/Shared/Icon.razor`.

## Show me

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `GenerateGuideAsync` | POST | `/guides` | multipart: `goal`, `photo?` | `Guide`, or `204 No Content` when there is no guide for that goal |
| `GetGuideAsync` | GET | `/guides/{id}` | – | `Guide` or 404 |
| `AskForHelpAsync` | POST | `/guides/{id}/help` | `{ stepNumber }` | 204 (adds to the activity log and tells the trusted person) |

`Guide`:

```
{
  id, title, goal,
  photoUrl: string|null,                 // the user's photo, served by the back end
  picture: string,                       // the generated "what it looks like when done" image (URL in a real back end; a drawing key in the mock)
  steps: string[],
  highlight: { x, y, width, height, label } | null,   // percent of the photo
  needsCare: { topic: "Chemicals"|"Medicines"|"Electrics", text } | null,
  madeAt
}
```

## Explain

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `ExplainContentAsync` | POST | `/explanations` | multipart or JSON: `text?`, `file?`, `level: VerySimple\|Simple\|Standard`, `format: Summary\|Steps\|Questions` | `Explanation` |
| `GetExplanationAsync` | GET | `/explanations/{id}` | – | `Explanation` or 404 |
| `AskAboutAsync` | POST | `/explanations/{id}/questions` | `{ question }` | `ChatAnswer { question, answer }` |

`Explanation`:

```
{
  id, title,
  overview: string,                      // "What this is and what you need to do"
  deadline: string|null,                 // "Reply by 14 November 2026"
  chunks: [ { heading, body } ],
  actions: string[],                     // the "Action needed" checklist
  originalText: string,
  hardWords: [ { start, length, meaning } ],   // offsets into originalText
  level, format
}
```

Changing the level or format calls `POST /explanations` again with the same `originalText`.

## Remember

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `GetMemoryCardsAsync` | GET | `/memories?search=` | – | `MemoryCard[]`, newest first |
| `GetMemoryCardAsync` | GET | `/memories/{id}` | – | `MemoryCard` or 404 |
| `AskMemoryAsync` | POST | `/memories/ask` | `{ question }` | `MemoryAnswer { question, answer, cards: MemoryCard[] (0–2) }` |
| `SuggestedMemoryQuestions` | GET | `/memories/suggested-questions` | – | `string[]` |
| `SaveGuideToMemoryAsync` | POST | `/memories/from-guide/{guideId}` | – | `MemoryCard` (saving twice returns the same card) |
| `SaveExplanationToMemoryAsync` | POST | `/memories/from-explanation/{explanationId}` | – | `MemoryCard` |
| `RenameMemoryAsync` | PATCH | `/memories/{id}` | `{ title }` | `MemoryCard` |
| `ShareMemoryAsync` | PATCH | `/memories/{id}` | `{ shared: bool }` | `MemoryCard` |
| `DeleteMemoryAsync` | DELETE | `/memories/{id}` | – | 204 |

`MemoryCard`:

```
{
  id, title, date: "yyyy-MM-dd", summary,
  photoUrl: string|null, picture: string,
  tags: string[], steps: string[],
  howYouDidIt: "Last time you reset the internet box by …",
  source: ShowMe|Explain,
  shared: bool
}
```

## Trusted person

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `GetReviewQueueAsync` | GET | `/review` | – | `ReviewItem[]` (held inbox mail and held Check items, newest first) |
| `GetReviewItemAsync` | GET | `/review/{id}` | – | `ReviewItem` or 404 (404 once decided) |
| `ResolveReviewItemAsync` | POST | `/review/{id}/decision` | `{ decision: Release\|Block\|CallFirst }` | 204, or 409 if already decided |
| `GetActivityAsync` | GET | `/activity?limit=10` | – | `ActivityEntry { at, icon, text }[]`, newest first |
| `GetSharedMemoriesAsync` | GET | `/memories?shared=true` | – | `MemoryCard[]` |

`ReviewItem`:

```
{
  id, source: Inbox|Check, title,
  preview: string,              // text only, links defanged: hxxp://example[.]com
  verdict, flags: RiskFlag[],
  reasoning: string,            // the model's reasons, in plain words
  callRequested: bool, at
}
```

`CallFirst` keeps the item held and sets `callRequested`.

## Photos

| Method | Path | Response |
|---|---|---|
| GET | `/mode-photo/{id}` | The image, with `X-Content-Type-Options: nosniff`. Served by this app in the mock. |

## Stories

| Function | Method | Path | Request | Response |
|---|---|---|---|---|
| `MakeStoryAsync` | POST | `/stories` | `StoryRequest { situation, when? }` | `Story` |
| `GetStoryAsync` | GET | `/stories/{id}` | – | `Story` or 404 |
| `GetStoriesAsync` | GET | `/stories` | – | `Story[]`, newest first |
| `DeleteStoryAsync` | DELETE | `/stories/{id}` | – | 204 |

`Story { id, title, when?, pages: [ { heading, text, picture } ], status: Ready|Held|Blocked, madeAt }`.
A sensitive topic comes back `Held` with no `pages` (and a generic title) until the trusted person decides. Held and
unclear results fail closed. `Blocked` is final and the user's endpoints return 404 for it. Held stories appear in the
trusted person's review queue as `source: Story`, and are decided with the same `Release` / `Block` / `CallFirst` call.
