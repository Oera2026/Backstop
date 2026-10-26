# AGENTS.md

Guidance for AI coding agents working on **Backstop**, a safe email client for elderly people and adults with learning disabilities.
App: .NET 10 Blazor Server in `Backstop/`. Plain CSS, no UI framework. Run: `cd Backstop && dotnet run`.

> The Astryx / React guidance that used to be in this file does not apply to this Blazor project. Do not add Astryx, Tailwind or any UI framework.

## Core idea
Backstop holds every incoming email until it is checked. The user never sees mail until it is cleared safe. Anything unclear goes to a trusted **helper**, who approves or blocks it.

## Hard rules
1. **Synthetic data only.** All people, messages and domains are made up. Use only `example.com`, `example.net`, `example.org` and `.test`. Never use real names, real addresses or real brands' domains.
2. **No secrets.** No API keys, tokens or passwords in the repo. `.env.example` holds empty placeholders only. No real email, no network calls, no backend logic in this shell.
3. **Held mail privacy.** Held mail shows NO sender and NO subject to the user. The user sees only "N message(s) being checked by your helper." The user can open a message only if its state is `Cleared` (`IMailService.GetClearedMessage`).
4. **Fail closed.** States: `Received, Scanning, Cleared, Held, Blocked, Failed`. Any error, unknown or unexpected state becomes `Held` (explicit `Failed -> Held`). Never let a message through on error. Blocked is final and invisible to the user.
5. **Mock behind interfaces.** UI talks only to `IMailService` and `IGuardianService`. Mock logic lives in `Services/Mock*` and `MockData`, with a comment where real logic will replace it.

## Theme "Calm Clinical"
- Colours are CSS variables in `wwwroot/css/theme.css` only. Do not hardcode hex values elsewhere.
- Base font 20px, line-height 1.6, headings 28-36px. Font: Inter, falling back to system-ui.
- Buttons: min-height 56px. Visible 3px focus ring on every interactive element.
- Style: Apple-like restraint, rounded cards, clean white, one blue accent.

## Accessibility and cognitive ease (strict)
- One decision per screen. At most two buttons: one primary (blue), one quiet secondary. (The helper evidence card is the one exception: Approve, Block, Call them first.)
- Status is always **icon + word + colour**, never colour alone: "Safe" (check), "Your helper is checking this" (hourglass), "Blocked" (cross). Use the `StatusBadge` component.
- Plain language, reading age about 9, short sentences, no jargon.
- "Read this to me" on message and explanation screens (browser `speechSynthesis` via `wwwroot/js/backstop.js`).
- Respect `prefers-reduced-motion`. Keyboard navigable. Proper landmarks, ARIA labels, semantic HTML.
- Responsive down to 360px. PWA manifest in `wwwroot/manifest.webmanifest`.
- Helper previews are text only and links are defanged (`hxxp://example[.]com`, see `Defang`). Never render a clickable link from message content.

## Layout
`Components/{Layout,Pages,Shared,Helper,Demo}`, `Models/`, `Services/`, `wwwroot/css/`.
Routes: `/` user, `/helper` helper, `/demo` demo panel (split screen uses iframes of `/` and `/helper`).

## Working rules
- Commit in small, meaningful commits.
- After changes, run the app and check all three views and all 7 scenarios in `/demo`.
