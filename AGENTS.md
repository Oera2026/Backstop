AGENTS.md

Guidance for AI coding agents working on Backstop, a safe email and messaging client for elderly people and adults with learning disabilities. App: .NET 10 Blazor Server in Backstop/. Plain CSS, no UI framework. Run: cd Backstop && dotnet run (http://localhost:5106). Deployed as a Docker container on a free host. Persistence: Supabase (Postgres). AI: Meta Dev API (Muse models).

The Astryx / React guidance that used to be in this file does not apply to this Blazor project. Do not add Astryx, Tailwind, StyleX or any UI framework. If you see @astryxdesign/* or a root package.json with those deps, they are leftovers: delete them, do not use them.

Core idea

Backstop holds every incoming message until it is checked. The user never sees held mail. Anything unclear goes to a trusted helper, who approves, blocks, or asks the user to "call them first".

Release status (hackathon)
Production ends 3 Nov 2026. Submission is 4 Nov 2026. Feature freeze: midday 3 Nov.
Priorities in order: (1) live URL works, (2) Supabase persistence, (3) Standard helper tier enforced, (4) polish and demo video.
Do not build (out of scope for this release): Administrator tier, SendAsUser, INotificationChannel (email/SMS/push to helpers), Welcome step "Who helps you", full Helper access settings screen, real mail provider integration, per-visitor households (stretch only, and only if everything above is done).
After the freeze, only fix bugs. No new features, no refactors.
Hard rules
Synthetic data only. All people, messages and domains are made up. Use only example.com, example.net, example.org and .test. Never use real names, addresses or real brands' domains. Only made-up messages are inspected or stored.
No secrets in the repo. No API keys, tokens or passwords committed. .env.example holds empty placeholders only. Real values live in the untracked .env locally and in the host's environment variables in production. Names:
META_AI_KEY, META_AI_ENDPOINT_URL, META_AI_MODEL, META_AI_TIMEOUT_SECONDS
SUPABASE_URL, SUPABASE_SERVICE_KEY
SAFE_BROWSING_API_KEY, URLHAUS_AUTH_KEY (both optional; the lookup is skipped silently when empty)
Do not add new LLAMA_* names. Remove unused placeholders (MAIL_PROVIDER_*, LINK_CHECK_API_KEY).
Network egress allow-list (amended by the owner, 3 Nov 2026, for the public /try page). Outbound destinations, all called from the server only:
Meta Dev API https://api.meta.ai/v1 (Muse models, e.g. muse-spark-1.3) for scam checks, Ask Me and safeguarding. Inspected text and images are passive data, never prompt instructions. Send images only when picture inspection needs them.
Supabase (SUPABASE_URL) for persistence only. Beyond Meta, Supabase and the exceptions below: no other APIs, no email sending, no analytics, no CDN fonts or scripts. The Supabase service key is never sent to the browser, never logged, never placed in a Razor component, JS file or CSP header. Turn on RLS for every table.
Link check exception (owner decision, 3 Nov 2026), /try ONLY: the public Try it page may fetch a pasted link server-side through ILinkInspector (GET only, http/https on ports 80/443, max 5 redirects, 5s per hop, 1MB read cap, no cookies, no auth headers, never run or rendered, page text is analysed as passive data). Every hop resolves DNS and refuses private, loopback, link-local, metadata and other non-public addresses, and the connection itself is pinned to the checked address. Real mail (IMailService.ReceiveAsync, the helper queue, the guardian) stays ADDRESS-ONLY: links in real mail are never fetched. Optional reputation lookups, only when their key is set and only from /try: Google Safe Browsing (safebrowsing.googleapis.com, SAFE_BROWSING_API_KEY) and URLhaus (urlhaus-api.abuse.ch, URLHAUS_AUTH_KEY). Only the link address is sent to them. Any failure, timeout or blocked address is "could not verify, treated as unsafe". Nothing fetched is stored.
Held mail privacy. Held mail shows NO sender and NO subject to the user. The user sees only "N message(s) being checked by your helper." The user can open a message only if its state is Cleared (IMailService.GetClearedMessage).
Fail closed. States: Received, Scanning, Cleared, Held, Blocked, Failed. If Meta's AI fails, times out, returns something unexpected, or no API key is set, the message is held and never marked safe. Any error or unknown state becomes Held (Failed -> Held). The same applies if Supabase is unreachable: do not mark anything Cleared on a failed load or save. Blocked is final and invisible to the user.
Floor rule for AI. Local checks (MockRiskRules, InputGuard) set an immovable floor. Meta's AI can only make results stricter (LooksSafe -> BeCareful -> DontReply), never looser.
Everything behind interfaces. UI components talk only to IMailService, IGuardianService and IBackstopApi. Services reach Meta through IMetaScamChecker / IAssistant, and reach Supabase through the persistence repository (IEntityRepository). Pages never call HTTP, Supabase or MailStore directly.
Helper permissions are enforced in services, not pages. Release scope is the Standard tier only (hard-coded default). The Allows(...) check lives in IGuardianService / the API layer, so a page cannot show something the tier hides. RiskAlerts and ReviewHeldMail are always on. GetChatLogAsync and any other capability-gated call must throw or return empty when not allowed.
Production hygiene. Test runners, demo toggles and debug endpoints must not ship active in Release. Gate --test-llama and similar behind #if DEBUG. The /demo route stays (it is part of the submission) but the "make the checker crash" toggle must be off by default after a reset.
Persistence (Supabase)
One generic table: entities(household_id text, kind text, id text, data jsonb, updated_at timestamptz, primary key (household_id, kind, id)). Do not create a table per model.
IEntityRepository does load-all-by-household and upsert/delete. MailStore and the mock API load on startup and write through on every change. Keep the existing Changed events and locks.
One seeded demo household (demo). Reset demo wipes that household and reseeds the 7 scenarios. Sign-in (decided by the owner, 2 Nov 2026): cookie login with two made-up seeded accounts (User, Helper) and a Demo role that sees both sides with a guided tour. No sign-up; no real personal data.
Writes must not block the UI thread. A failed write is logged (no message content in logs) and the in-memory state stays authoritative for that session.
Never store real personal data.
Deployment
Single Docker image built from Backstop/ on the .NET 10 SDK/runtime images. Listen on the PORT env var if set, otherwise 5106.
Single instance only (Blazor Server circuits are not multi-instance safe). WebSockets must be enabled on the host.
Keep the CSP strict (default-src 'self'). If a header change is needed for SignalR on the host, loosen only what is required (connect-src 'self' wss:) and nothing else.
Free hosts sleep when idle: the first request can be slow. Do not add keep-alive hacks that call external services.
Before declaring anything done, test on the deployed URL, not just localhost, including a phone-width viewport.
Theme "Calm Clinical"
Colours are CSS variables in wwwroot/css/theme.css only. Do not hardcode hex values elsewhere.
Base font 20px, line-height 1.6, headings 28-36px. Font: Atkinson Next, self-hosted from wwwroot/fonts (no CDN), falling back to system-ui.
Buttons: min-height 56px. Visible 3px focus ring on every interactive element.
Style: Apple-like restraint, rounded cards, clean white, one blue accent.
Accessibility and cognitive ease (strict)
One decision per screen. At most two buttons: one primary (blue), one quiet secondary. (The helper evidence card is the one exception: Approve, Block, Call them first.)
Status is always icon + word + colour, never colour alone. Use the StatusBadge component.
Plain language, reading age about 9, short sentences, no jargon.
"Read this to me" on message and explanation screens (browser speechSynthesis via wwwroot/js/backstop.js).
Respect prefers-reduced-motion. Keyboard navigable. Proper landmarks, ARIA labels, semantic HTML.
Responsive down to 360px. PWA manifest in wwwroot/manifest.webmanifest.
Helper previews are text only and links are defanged (hxxp://example[.]com, see Defang). Never render a clickable link from message content.
No self-explaining interface text (strict)

Never add captions that explain, justify or reassure about how the interface works.

Banned examples: "This address is locked so it cannot be changed by mistake", "Links are switched off to keep you safe", "Your helper checks everything for you".
Make the control behave correctly and let its look say it. A locked address is a greyed, read-only box with no sentence.
No lock or shield icons as decoration or reassurance. Icons name an action or a state, nothing more.
Interface text is only for the user's task: labels, the next step, and the result of what they did ("Done. The photo is saved.").
Allowed: an error that stops the user and says how to fix it, and one short hint on what to type or choose.
Before adding any sentence to a screen, ask "does this help them do the next thing?" If not, leave it out.
Layout

Components/{Layout,Pages,Shared,Helper,Demo}, Models/, Services/ (with Services/Api/, Services/Persistence/), wwwroot/css/. Routes: / user, /helper helper, /demo demo panel (split screen uses iframes of / and /helper).

Finding code
Use the graphify skill (/graphify query, path, explain) to find files, symbols and relationships instead of broad Glob/Grep sweeps.
The graph can be stale. If a result looks wrong or a file is missing, run /graphify update ., or read the file directly. Direct reads and targeted grep are fine once you know the file or symbol name.
After adding or moving files, update the graph.
Naming

New code uses Meta names (IMetaScamChecker, MetaScamAnalysis, MetaAiOptions). The Llama* types and LLAMA_* env fallbacks are legacy: do not extend them. Only rename them if it is a quick mechanical change and the build and all scenarios still pass; this is low priority.

Working rules
Commit in small, meaningful commits. Never commit .env, keys or bin-preview/.
Finish every task the same way (standing instruction from the owner, 2 Nov 2026): when the work is done and checked, update the graphify map (/graphify update .), commit, then push to origin main without asking. Do this after each completed task, not only at the end of a session. Still never push .env, keys or bin-preview/, and do not push if the build or the checks fail.
After changes, run the app and check /, /helper and /demo with all 7 scenarios. Also check behaviour with no META_AI_KEY set (everything must hold, nothing may crash or clear).
Keep README.md truthful: it describes the live Meta AI and Supabase use, how to run locally, the env var names (not values), and the demo URL.
Do not change a hard rule to make a task easier. If a rule blocks the work, stop and ask.