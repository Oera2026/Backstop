# PROJECT STATE & ARCHITECTURE REPORT

Generated 2026-11-01 from commit `2dc315c`. Updated 2026-11-03: the public Try it page (`/try`) is added (section 4). Other sections still describe 1 Nov and are partly out of date (sign-in cookies, the Render deployment and the Dockerfile now exist; Supabase persistence is still not wired).

## 1. Executive Summary & Core Stack

- **Project Purpose:** **Backstop** is a safe email and messaging client for elderly people and adults with learning disabilities. Every incoming message is held until checked. The user never sees held mail (no sender, no subject, only "N messages being checked by your helper"). Anything unclear goes to a trusted **helper**, who approves, blocks, or asks to "call them first". Around the mail core sit assistive modes: Check (scam check), Show me (step guides), Explain, Remember (memory cards), Stories (social stories), Ask Me (AI chat), and Chat with approved people.
- **Tech Stack:**
  - Frontend and backend: .NET 10 Blazor Server (interactive server render mode), Razor components, plain CSS (no UI framework), small vanilla JS (`wwwroot/js`).
  - Database: none. All state is in memory (`MailStore`, `MockBackstopApi`). Data is synthetic.
  - Third-party API: Meta Dev API (`https://api.meta.ai/v1/chat/completions`) with Muse models (default `muse-spark-1.3`). It is the only permitted external call.
  - Deployment: none yet. Local `dotnet run` on port 5106. PWA manifest is present.
  - Tooling noise: root `package.json` holds Astryx (`@astryxdesign/*`, stylex) deps left from a React template. AGENTS.md says they do not apply. Graphify and Impeccable skills are used for code navigation and design critique.
- **Current Development Phase:** Feature-complete front-end MVP with a mock backend, plus a real Meta AI integration for scam checking and Ask Me. Now in UX polish and hardening (layout critique, phone fixes, motion). Helper permission tiers are partly built (model and API in place, setup UI and enforcement not yet).

## 2. Directory Structure & File Inventory

```
Meta Hackathon/
├── AGENTS.md / GEMINI.md        Agent rules (hard rules, theme, a11y, copy rules)
├── README.md, PRODUCT.md, DESIGN.md, API_CONTRACT.md
├── docs/helper-permissions-plan.md   Plan: risk alerts + helper tiers
├── .env.example                 Placeholders only (real .env is git-ignored)
├── package.json                 Astryx deps only (unused by the Blazor app)
├── .claude/ (launch.json, skills/impeccable)   .impeccable/ (design.json, critiques)
├── graphify-out/                Knowledge graph (built at 0ab498b, stale)
└── Backstop/
    ├── Program.cs               DI, .env loader, CSP/security headers, photo endpoints, --test-llama (Debug builds only)
    ├── Backstop.csproj          net10.0
    ├── Models/                  MailMessage (+MessageState), Scenario, Modes (Check/Guide/Explain/Memory),
    │                            Ask, Chat, Stories, Extras (Contact, Calendar, Sent, HelperNotice, HeldOutgoing),
    │                            Permissions (HelperCapability/Tier/Permissions)
    ├── Services/
    │   ├── Interfaces.cs        IMailService, IGuardianService
    │   ├── MailStore.cs         In-memory state + change events
    │   ├── MockMailService.cs   Mock pipeline (tiers, Failed->Held), compose, contacts, calendar, search
    │   ├── MockGuardianService.cs, MockData.cs (7 scenarios, contacts, seed)
    │   ├── Defang.cs, InputGuard.cs, ImageSniff.cs, NoReply.cs, UserClock.cs
    │   └── Api/
    │       ├── IBackstopApi.cs  Contract for all modes (see API_CONTRACT.md)
    │       ├── MockBackstopApi (.cs, .Ask, .Chat, .Explain, .Stories), MockRiskRules.cs
    │       ├── Fixtures/        Chat, Check, Explain, Guide, Memory, Story fixtures
    │       ├── LlamaScamChecker.cs  MetaAiScamChecker + MetaAiOptions (real HTTP)
    │       ├── ILlamaScamChecker.cs (IMetaScamChecker + legacy alias)
    │       ├── MetaAssistant.cs, IAssistant.cs   Ask Me via Muse
    │       └── LlamaScamCheckerTests.cs   Custom test runner (not xUnit)
    ├── Components/
    │   ├── App.razor, Routes.razor, _Imports.razor
    │   ├── Layout/ (UserLayout, HelperLayout, DemoLayout, WelcomeLayout, TryLayout, AppNav, ReconnectModal)
    │   ├── Pages/User/   Home, Inbox, MessageView, Compose, Sent(View), Contacts, ContactEdit, Calendar(New),
    │   │                 Search, Check(+ResultView), Explain(+View), ShowMe, GuideView, Remember, MemoryView,
    │   │                 Stories, StoryNew, StoryView, MakeSense, AskMe, Messages, Conversation, Help, Welcome
    │   ├── Pages/Helper/ Queue, Evidence, CheckReview, StoryReview, HelperMessages, HelperSettings
    │   ├── Pages/Demo/DemoPanel.razor   Pages/Try.razor (public /try)   Pages/Settings.razor, Error, NotFound
    │   ├── Helper/ ActivityLog, CheckQueue, EvidenceCard, SharedMemories, StoryQueue
    │   ├── Demo/PipelineTimeline.razor
    │   └── Shared/       StatusBadge, VerdictCard, Icon, ReadAloudButton, ChatLog/Composer, CameraCapture,
    │                     PicturePicker, ErrorState, LoadingState, LiveComponentBase, ModeComponentBase, etc.
    └── wwwroot/  css/{theme,app,modes,motion}.css, js/{backstop,camera,settings}.js, fonts (Atkinson Next),
                  icons, manifest.webmanifest
```

Roughly 4,000 lines of C# in `Services/` and `Models/`, about 100 Razor components and pages, and 4 CSS files.

## 3. Architecture & Data Flow

- **System Architecture:** Browser <-> SignalR circuit (Blazor Server) <-> Razor pages. Pages talk only to `IMailService`, `IGuardianService`, `IBackstopApi` (AGENTS rule 7). Mock singletons implement them over in-memory stores. `MetaAiScamChecker` and `MetaAssistant` (typed `HttpClient`, no redirects, no cookies) call Meta. Singleton `Changed` events plus `LiveComponentBase` push live updates between the user, helper, and demo views, which are the same server process (the demo's split screen uses iframes of `/` and `/helper`). Routes: `/` user, `/helper` helper, `/demo` demo panel, `/try` public check-an-email page.
- **Mail pipeline:** `Received -> Scanning -> (Cleared | Held | Blocked)`. Any error or unknown state becomes `Failed -> Held` (fail closed). The pipeline runs 4 tiers (Rules, Meta AI, Link check, Policy); no timings are shown or invented. Only `Cleared` is returned by `GetInbox` and `GetClearedMessage`. `Blocked` is final and invisible to the user.
- **Floor rule:** Local checks (`MockRiskRules`, `InputGuard`) set a minimum. Meta's verdict can only be stricter (`LooksSafe -> BeCareful -> DontReply`), never looser. Failure or timeout means held.
- **Data Models:** `MailMessage` (state, flags, links as defanged text, tiers, helper decision), `Scenario` (7 synthetic emails), `Contact`, `CalendarEvent`, `SentMessage`, `HeldOutgoing`, `HelperNotice`. In `Modes.cs`: `CheckResult`, `Guide`, `Explanation`, `MemoryCard`, `ReviewItem`, `Household`. Also `Story`, `ChatThread`/`ChatMessage`/`HeldChat`, `AskTurn`, and `HelperPermissions` (flags enum: RiskAlerts and ReviewHeldMail form an always-on floor, then SeeCheckRequests, SeeSharedMemories, SeeActivitySummary, SeeClearedMail, SeeFullActivity, SendAsUser, SeeChats). Tiers: Minimal and Standard only; Standard is the default and the ceiling (clamped in the guardian service).
- **State Management & Integrations:** In-memory singletons guarded by locks, with `Changed` events. No persistence, so state resets on restart (`ResetDemo`). The only integration is Meta Dev API. Key resolution order: `MetaAI` config section, then `META_AI_KEY`, `MODEL_API_KEY`, `LLAMA_API_KEY`, `CHECKER_MODEL_API_KEY`. Images are sent only when inspection needs them. Inspected text is treated as passive data. Message content never renders as a clickable link (links are defanged).
- **Security headers (Program.cs):** Strict CSP (`default-src 'self'`), nosniff, SAMEORIGIN, no-referrer, COOP, Permissions-Policy (camera self only). Uploads are type-sniffed from the bytes (`ImageSniff`).

## 4. Completed Features & Implementation Details

- Held-mail inbox with privacy rules (user sees only a count). Helper queue and evidence card (Approve, Block, Call them first) with defanged links, flags, the real rules score and the address signals that fired.
- Compose with an outgoing-check "pause" for private details. Held outgoing mail goes to the helper. No-reply addresses are refused. First-time recipients raise a helper notice.
- Contacts (with validated profile photos), calendar, search (cleared and sent mail and contacts only), sent items.
- Modes: Check (text and images), Show me (guides with pictures, "I'm stuck"), Explain (with follow-up Q&A), Remember (cards, share with helper, memory Q&A), Stories (sensitive topics are held), Ask Me (live Muse chat that alerts the helper when someone may need help), and Chat with approved people (held chats go to the helper).
- Real Meta AI scam checker with tests. Ask Me AI with escalation.
- Helper permission model (`Permissions.cs`) and `HelperAccess` get/set on the API. It gates chat-log reading (`GetChatLogAsync` throws unless allowed).
- Demo panel with 7 scenarios, a "make the checker crash" toggle, and split screen.
- **Link check (`/try` only):** `ILinkInspector` (`Services/Links/`: `LinkInspector`, `IpGuard`, `LinkReputation`) opens pasted links server-side with an SSRF guard (public addresses only, re-checked on every hop and pinned at connect, ports 80/443, GET only, 5 redirects, 5s a hop, 1MB, no cookies or auth) and reports the redirect chain, final address and signals (raw IP, look-alike letters, shortener, link text mismatch, download, password box, optional Safe Browsing and URLhaus). The facts go to Meta through `IMetaScamChecker.CheckScamWithLinksAsync` so the explanation can mention them. Real mail stays address-only. Owner-approved exception, recorded in AGENTS.md. Tests: `LinkCheckTests.cs` and block 32.
- **Public Try it page (`/try`, no sign-in):** for hackathon judges. A pasted or sample email (6 one-click examples) gets a verdict (Blocked, Held for helper, Safe), flagged signals, a plain AI explanation and a "what your helper sees" line. It calls `IMailService.TryCheckAsync` (`MockMailService.Try.cs`), which runs the same `MailAnalyzer`, Meta checker and `CheckOutgoing` as real mail but stores nothing, never opens links (defanged text only), caps input at 5,000 letters and 6 checks a minute per visitor, and holds (never Safe) when Meta AI gives no answer. Samples are in `Services/TryData.cs`; layout `TryLayout.razor`; page `Pages/Try.razor`; linked from `/login` and the README. Tests: block 31 in `--test-llama`.
- Theme "Calm Clinical": CSS variables in `theme.css` only, 20px base, Atkinson Next font, 56px buttons, 3px focus ring, reduced-motion support, and read-aloud via `speechSynthesis`. `StatusBadge` combines icon, word, and colour. Welcome flow with text size and colour steps.
- Design decisions: no self-explaining UI text, no lock or shield decoration, and no UI framework.

## 5. In-Progress & Work-in-Progress (WIP)

- **Helper permissions plan** (`docs/helper-permissions-plan.md`) is only partly built:
  - Done: the capability and tier model, and `HelperAccess`/`SetHelperAccess` on the API.
  - Not done: the risk-alert pipeline trigger, extended `HelperNotice` (Kind, Severity, MessageId), `INotificationChannel`, `IPermissionService`, `IGuardianService` capability checks, the Welcome step "Who helps you, and how much?", the Settings "Helper access" screen, and `SendAsUser` with Administrator safeguards (extra confirm, marked in Sent, activity log).
- **Mocks:** all mail, guardian, and mode data. `MockMailService.Send` pretends to send. Photos are served from memory. There is no real mail provider. `MAIL_PROVIDER_CLIENT_ID/SECRET` and `LINK_CHECK_API_KEY` in `.env.example` are unused placeholders.
- **Real vs mock mix:** `MockBackstopApi` is the only `IBackstopApi`. Scam checks and Ask Me use Meta only when a key is set. Confirm what the demo does without a key (expected: fail closed or held).
- README still says "no API calls", which is outdated.

## 6. Known Technical Debt, Bugs & Blockers

- **Tests:** no xUnit or CI. `LlamaScamCheckerTests.cs` is a hand-rolled runner inside the app assembly, run with `dotnet run -- --test-llama`. It is wrapped in `#if DEBUG`, so it does not exist in Release. No tests for mail pipeline, permissions, or UI.
- **Naming debt:** `LlamaScamChecker.cs`, `ILlamaScamChecker`, `LlamaScamAnalysis`, and `LLAMA_*` env fallbacks are legacy aliases after the move to Meta Muse. Duplicate record types (`MetaScamAnalysis` and `LlamaScamAnalysis`).
- **Permissions enforcement gap:** `Allows` is checked in few places. Pages may show things the tier should hide, and the guardian service is not yet the gate.
- **Persistence and scale:** in-memory singletons are shared by all users and circuits (one household). There is no auth, no multi-user model, and no database. `MockBackstopApi` is about 660 lines and is a god object (123 graph edges).
- **Graph and docs stale:** `graphify-out` was built at `0ab498b` and needs `graphify update .`. README, API_CONTRACT, and AGENTS should be checked against recent commits.
- **Cleanup:** root Astryx and node_modules are unused. `Backstop/bin-preview` is a build artifact. `GEMINI.md` duplicates agent guidance.
- **Open decisions:** whether email, SMS, or push alerts to helpers are allowed. Hard rule 3 forbids external calls beyond Meta. Whether the live API has been exercised end to end with a real key is not recorded in the repo.
- No TODO/FIXME comments found in `.cs` or `.razor` files.
- Last manual verification of all three views and 7 scenarios is not recorded.

## 7. Configuration & Environment Context

- **Environment variables (names only):**
  - `META_AI_KEY` (alternates: `MODEL_API_KEY`, `LLAMA_API_KEY`, `CHECKER_MODEL_API_KEY`)
  - `META_AI_ENDPOINT_URL` (default `https://api.meta.ai/v1/chat/completions`)
  - `META_AI_MODEL` (default `muse-spark-1.3`; alternates `LLAMA_MODEL`, `muse-spark-1.3`, `muse-wonder-1.0`)
  - `META_AI_TIMEOUT_SECONDS` (placeholder in `.env.example`; code default 30)
  - Unused placeholders: `MAIL_PROVIDER_CLIENT_ID`, `MAIL_PROVIDER_CLIENT_SECRET`, `LINK_CHECK_API_KEY`
  - `.env` is read from the repo root (`Backstop/../.env`) at startup and is git-ignored. Config section `MetaAI` is also supported.
- **Run / build:**
  - `cd Backstop && dotnet run` -> http://localhost:5106
  - `.claude/launch.json`: `dotnet run --project Backstop --urls http://localhost:5106 -p:BaseOutputPath=bin-preview/`
  - Meta checker tests: `cd Backstop && dotnet run -- --test-llama`
  - Requires the .NET 10 SDK. There is no Makefile and no real npm scripts (`npm test` is a stub).
  - After changes (AGENTS.md): run the app and check `/`, `/helper`, and `/demo` with all 7 scenarios.
- **Hard rules (AGENTS.md):** synthetic data only (`example.com/.net/.org`, `.test`), no secrets in the repo, Meta API is the only network egress, held mail shows no sender or subject, fail closed, AI can only tighten verdicts, and the UI talks only to the service interfaces.

## 8. Sender check (added 2026-11-03)

- `Services/SenderCheck.cs`: `ISenderCheck` / `SenderCheck`. Pure code, never throws. Returns `SenderSignal(Code, Severity, Reason)` (Low, Medium, High) from the From name, From address, Reply-To and the saved contacts. Signals: `brand-name-wrong-address`, `family-name-free-address`, `family-name-unknown-address`, `free-address-person`, `name-matches-contact`, `name-shows-other-address`, `lookalike-domain` (0/o, 1/l, rn/m, vv/w, edit distance, brand joined by a dash), `brand-in-subdomain`, `brand-other-domain`, `idn-homograph` (punycode or Unicode), `reply-to-differs`, `reply-to-lookalike`, `unverified-sender`. Reference lists (about 35 brands, free providers) are in the same file.
- Policy: `MailAnalyzer.Analyze(mail, contacts, senderCheck)`. A High signal sets `SenderProblem`, so the message can never be Cleared (held for a helper). Medium and Low are evidence only. Nothing lowers a verdict. Unreadable headers give a High `unverified-sender` (fail closed).
- AI evidence: `IMetaScamChecker.CheckScamWithEvidenceAsync(text, linkFindings, senderFindings)`. `MetaAiScamChecker` adds a "BACKSTOP SENDER CHECK" section after the data (folded, cut, edge lines neutralised).
- Model: `InboundMail.ReplyTo`, `MailMessage.ReplyTo`, `MailMessage.SenderSignals`, `TryResult.SenderChecks`.
- UI: `Shared/SenderChecks.razor` shown as "Sender checks" on the helper evidence card and the `/try` result; `/try` has an optional Reply-to box.
- Demo: scenarios 8 to 11 (`mum-new-address`, `lookalike-bank`, `reply-to-mismatch`, `contact-new-address`) and four matching `/try` examples. They use made-up stand-ins only (`freemail.example.net`, the fictional brand Northbank at `northbank.test`), because the hard rules allow no real domains in messages. The detection lists do name real brands and free providers; they are compared as text and never sent anywhere.
- Same-company rule: a saved contact's name from another address at the same non-free domain (reception@ vs reminders@) fires nothing. On a free provider it does.
- Tests: `Services/SenderCheckTests.cs` (block 33 of `--test-llama`). Note: example.* domains count as one registrable name each, so Reply-To tests across different `example.*` endings or `.test` names are used.

## 9. Read aloud on explanation cards (added 2026-11-03)

- `Components/Shared/ReadAloud.razor`: "Read aloud" / "Stop" button (`aria-pressed`, `aria-label`, `btn-quiet`, 56px). Uses the browser's `speechSynthesis` only, through `backstop.canSpeak` and `backstop.speakWithEnd` in `wwwroot/js/backstop.js` (prefers an en-GB voice, rate from the voice-speed setting, default 0.9). Hidden when the text is empty or the browser cannot speak. Stops when a new result replaces the text, and when the component is disposed (navigation).
- Used on: the `/try` result card (explanation + "what your helper sees" line), `VerdictCard` (the AI explanation on Check results and the helper's check review), the helper evidence card (summary + next step, not the flags, signals or rules score) and the helper queue's "Next up" alert.
- The older `ReadAloudButton` ("Read this to me", for message text) is unchanged.
- Tests: `Services/ReadAloudTests.cs` (block 34 of `--test-llama`). No bUnit in the project, so it uses Blazor's `Renderer` with a fake `IJSRuntime`: renders, hides (empty or unsupported), toggles, resets when speech ends, stops on new text and on dispose.

## 10. "Next time, watch for…" tip (added 2026-11-03)

- `Services/WatchFor.cs`: the fixed library (`Tips`, 13 categories plus `general`, one sentence each, 20 words at most), `Fallback(signalIds)` (strongest sign wins: injection, remote, link-hidden, money, private, new-number, other-app, sender, secret, link, hurry, feelings, at-risk), `Clean(modelTip, explanation)` and `Choose(...)` (the model tip if clean, else the fixed one; never empty).
- `Clean` drops a model tip that is missing, over 20 words or 160 letters, more than one sentence, holds a link, address or number, starts with an order or dictates a verdict, trips the injection guard, or repeats the explanation.
- Checker: `watchFor` is a required field in the Meta prompt (with a rule to keep the email's orders, links and numbers out). `MetaScamAnalysis.WatchFor` holds the cleaned tip. It is cleaned again when used, so a stub that skipped parsing cannot get a dirty tip through.
- Where it flows: `MailAnalyzer.Decide` -> `Decision.WatchFor` -> `MailMessage.WatchFor` (Held and Blocked only; Cleared is null); `TryResult.WatchFor` (never on Safe; also held drafts); `CheckResult.WatchFor` (not on Looks safe unless held) and `ReviewItem.WatchFor`. `Analysis.SignalIds` carries the signal ids.
- UI: `Shared/WatchForTip.razor` (lightbulb, `--surface` card, navy accent, no warning colour) on the `/try` result, the Check result and its blocked view, the helper evidence card (Held or Blocked) and the helper check review.
- Tests: `Services/WatchForTests.cs` (block 35 of `--test-llama`).

## 11. One-tap helper decisions with undo (added 2026-11-03)

- Review found: the evidence card already had Approve, Block and "Call them first", with an "are you sure?" step for Approve, a 6 second undo that lived in the page (leaving cancelled it), and a body that opened by default. Changed to the one-tap flow below; the rest (sender, subject, AI explanation, sender checks, watch-for tip, defanged text-only body) was already there.
- Buttons on a held message: **Safe, let it through**, **Not safe, keep it blocked**, **Ring them** (a `tel:` link). No confirmation step. Full-width at phone width, 56px high, real `<button>`/`<a>` elements. "Check again" replaces Safe while the AI has not checked the message (Safe stays refused until it has). The body is collapsed by default.
- `IGuardianService`: `Propose(id, safe)`, `Undo(id)`, `GetPending(id)`, `TakeFailure(id)`, `GetRingHref()`, `DecisionWindowSeconds` (10). The decision waits out the window **in the service** (a one-second-scale timer calls `CommitDue`), so nothing changes for the user until it ends, "Blocked is final" is kept (an undo never un-blocks or recalls mail anyone saw), and leaving the page does not lose the decision. A later tap replaces the pending one. `Approve`/`Block` still work as the immediate form and share the same save path.
- Fail closed: if saving throws or is refused, the message stays Held, the pending decision is dropped and the card shows "We could not save that. The message is still held, so please try again." (once, via `TakeFailure`). Test seam: `SaveCheck`.
- Ring them: no phone number is stored anywhere in the app and none is collected, so `GetRingHref()` returns null and the button is hidden. `MockGuardianService.UserPhone` is the one place a saved number would be read from; it is cleaned to digits and `+`. Because of this, "Call them first" is no longer reachable from the card (the `CallFirst` service method and its tests are unchanged).
- What the user sees: an approved message shows "Your helper checked this one. It's safe." (`MessageView`); a block shows "Your helper blocked this one." on the inbox (`IMailService.RecentHelperBlocks`, a count for the last day, nothing else about the message). Held mail is still never visible.
- No memory backlog exists in the project, so a "Not safe" decision feeds nothing back.
- Tier: unchanged. `SendAsUser` and `SeeChats` stay off, asking for a higher tier is clamped to Standard, and the tests now assert this after a decision.
- Tests: `Services/HelperDecisionTests.cs` (block 36 of `--test-llama`), with a fake clock and the timer switched off.
