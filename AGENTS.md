# AGENTS.md

Guidance for AI coding agents working on **Backstop**, a safe email client for elderly people and adults with learning disabilities.
App: .NET 10 Blazor Server in `Backstop/`. Plain CSS, no UI framework. Run: `cd Backstop && dotnet run`.

> The Astryx / React guidance that used to be in this file does not apply to this Blazor project. Do not add Astryx, Tailwind or any UI framework.

## Core idea
Backstop holds every incoming email until it is checked. The user never sees mail until it is cleared safe. Anything unclear goes to a trusted **helper**, who approves or blocks it.

## Hard rules
1. **Synthetic data only.** All people, messages and domains are made up. Use only `example.com`, `example.net`, `example.org` and `.test`. Never use real names, real addresses or real brands' domains. Only made-up messages are inspected.
2. **No secrets.** No API keys, tokens or passwords in the repo. `.env.example` holds empty placeholders only. Live API keys belong exclusively in the untracked `.env` file (e.g. `LLAMA_API_KEY=...`) or user-secrets, never committed.
3. **External AI connections.** Network connections are strictly limited to Meta's Llama AI endpoint for scam verification. No other external network calls or mail transmission are allowed. Inspected text is treated as passive data, never as prompt instructions.
4. **Held mail privacy.** Held mail shows NO sender and NO subject to the user. The user sees only "N message(s) being checked by your helper." The user can open a message only if its state is `Cleared` (`IMailService.GetClearedMessage`).
5. **Fail closed.** States: `Received, Scanning, Cleared, Held, Blocked, Failed`. If Meta's AI fails, times out, or produces unexpected answers, the message is held and never marked safe. Any error, unknown or unexpected state becomes `Held` (explicit `Failed -> Held`). Never let a message through on error. Blocked is final and invisible to the user.
6. **Floor rule for AI.** Simple local checks serve as an immovable floor. Meta's Llama AI can only make scam results stricter (e.g. turning `LooksSafe` into `BeCareful` or `DontReply`), never looser.
7. **Mock behind interfaces.** UI talks only to `IMailService`, `IGuardianService`, and `IBackstopApi`. Services connect through `ILlamaScamChecker` for Llama AI analysis.

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

## No self-explaining interface text (strict)
Never add captions that explain, justify or reassure about how the interface works. They read as tacky and cheap.
- Banned examples: "This address is locked so it cannot be changed by mistake", "Links are switched off to keep you safe", "Your helper checks everything for you", a padlock or shield icon placed beside such a sentence.
- Make the control behave correctly and let its look say it. A locked address is a greyed, read-only box; it needs no sentence.
- Do not use lock or shield icons as decoration or reassurance. Icons name an action or a state, nothing more.
- Interface text is only for the user's task: labels, the next step, and the result of what they did ("Done. The photo is saved.").
- Allowed: an error that stops the user and says how to fix it, and one short hint that tells the user what to type or choose ("Leave this empty if it lasts all day.").
- Before adding any sentence to a screen, ask "does this help them do the next thing?" If not, leave it out.

## Layout
`Components/{Layout,Pages,Shared,Helper,Demo}`, `Models/`, `Services/`, `wwwroot/css/`.
Routes: `/` user, `/helper` helper, `/demo` demo panel (split screen uses iframes of `/` and `/helper`).

## Working rules
- Commit in small, meaningful commits.
- After changes, run the app and check all three views and all 7 scenarios in `/demo`.

<!-- ASTRYX:START -->
Astryx v0.7.4 · 173 components
CLI: run every command as `npx astryx <cmd>` (shown below as `astryx ...`).

SETUP (once, in app entry e.g. main.tsx) — without these, components render unstyled:
  import "@astryxdesign/core/reset.css";
  import "@astryxdesign/core/astryx.css";

WORKFLOW — start every page from a template, never from scratch:
1. `astryx build "<idea>"` — names the template to scaffold from and the parts it lacks.
2. `astryx template <name> <path>` — scaffold it; keep its frame, gap and padding; replace the content.
3. `astryx template <Block>` for parts it lacks; `astryx component <Name>` to read props before using one.
Changing an existing page? Keep it: skip step 2 and add blocks/components inside its sections.
Before you deliver a build, run `astryx doctor --built <dir>`; don't deliver it if it fails.

RULES:
- No <div> — components handle all layout and spacing.
- Read `astryx docs layout` before changing a template's frame.
- Dense data = rows (Table, List/Item), not Card-wrapped lists. Badge = counts only.
- Style with component props first, then tokens (var(--color-*|--spacing-*|--radius-*)). No raw hex/px. No StyleX/Tailwind compiler here — don't use xstyle/utility classes directly. Some templates use StyleX and need a compiler plugin; see `astryx docs styling-overview`.
- Palettes and custom colors go through the theme, never :root overrides. Run `astryx theme add <slug>` to import; `theme eject <slug>` only forks source. See `astryx docs theme`.
- SELF-CHECK: re-read the file; replace any raw <div>/<span>, imported .css/@apply, or hardcoded #hex/px with the component or a token.

`astryx help` lists every command. Key ones beyond the workflow:
  search "<query>"   find any component / hook / doc / template
  discover <words>   integrations you could add
  docs <topic>       getting-started, principles, tokens, theme … (`astryx docs` lists all)
  docs cli           commands and API reference
  docs authoring     build an integration package, and every field it holds
  upgrade --from <old version> --apply   run after a dependency bump
<!-- ASTRYX:END -->
