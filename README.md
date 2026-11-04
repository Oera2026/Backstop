# Backstop

A safe email and messaging client for elderly people and adults with learning disabilities.
Backstop holds every incoming message until it is checked. The user never sees held mail. Anything unclear goes to a trusted person (the **helper**), who approves it, blocks it, or asks the user to call the sender first.

> All data is synthetic: made-up people, messages and domains (`example.com`, `example.net`, `example.org`, `.test`). There is no real email provider. No secrets are in this repo.

**Live URL:** https://backstop-301g.onrender.com/home

Built with .NET 10 Blazor Server and plain CSS. No UI framework.

## How it works

Every incoming message goes through four checks, in order: **Rules**, **Meta AI**, **Link check**, **Policy**.

Alongside the Rules, a **Sender check** (`ISenderCheck`, pure code, no AI call) looks at who the message says it is from: the name shown against the address (a brand, a family word like "Mum", or a saved contact on a different address), look-alike and punycode domains, and a Reply-To on another domain. Its findings go to Meta AI as evidence and show in a "Sender checks" section for the helper and on `/try`. A serious finding always holds the message, whatever Meta AI says. Headers that cannot be read are treated as unverified and held.

- **Cleared** only when the local checks are clean and Meta AI answered and agreed.
- **Held** for the helper when anything looks wrong, or when a check fails. An email that gives orders to the checker ("ignore your instructions…") goes straight to the helper with no AI call: it is never blocked unseen, because a person should decide.
- **Blocked** (final, invisible to the user) only for a disguised link together with a strong warning sign, and never when someone may be at risk.
- **Fail closed.** A Meta error, a timeout, an unexpected answer, a missing API key or any exception ends in Held. Nothing is ever cleared on a failure.
- **Local checks are a floor.** The phrase rules and input guard set a minimum. Meta's answer can only make a result stricter, never looser.
- **Links in real mail are never opened.** They are judged by their address only, and shown defanged (`hxxp://example[.]com`). Message content never renders as a clickable link.
- **Held mail is private.** The user sees only "N message(s) being checked by your helper", with no sender and no subject.

## Meta API integration

Backstop calls the Meta Dev API (`https://api.meta.ai/v1`, Muse models such as `muse-spark-1.3`) from the server only, for:

- scam checks on messages, conversations and pictures,
- Ask Me,
- safeguarding (spotting when someone may need help).

Text and images being inspected are passed as data, never as instructions. Images are sent only when a picture check needs them. Any endpoint other than `https://api.meta.ai` is refused before a request is sent.

Calls retry twice with backoff, honour `Retry-After`, and pause for 20 seconds if most recent calls fail. A per-minute call cap (default 40, shared by all visitors) holds items instead of calling Meta when it is exceeded. With no `META_AI_KEY`, every check fails closed and Ask Me says it cannot answer.

Explain and questions about a document go to Meta AI. Photos of a document are read by the AI; PDFs cannot be read yet, so Explain asks for a photo or pasted text. Show me guides and Remember questions also go to Meta AI: guides are written fresh for any goal (unsafe goals are refused), and memory answers are written only from the person's saved cards. Guide and story pictures are simple line drawings the app makes itself, from a picture the AI picks — that is the chosen art style, not generated imagery.

## Persistence (Supabase)

Persistence is planned as one generic Supabase table, `entities`, with row-level security on. `SUPABASE_URL` and `SUPABASE_SERVICE_KEY` are reserved for it and are read on the server only. **It is not wired up yet:** today all state is in memory and resets when the app restarts.

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cp .env.example .env   # then fill in META_AI_KEY (optional, see below)
cd Backstop
dotnet run
```

Open http://localhost:5106. The app runs without a key: everything is held, nothing is cleared, nothing crashes.

## Configuration

Set these as environment variables (locally in the untracked `.env`, in production in the host's settings). `.env.example` lists the names with empty values.

| Name | What |
|---|---|
| `PORT` | Port to listen on. Defaults to 5106. |
| `META_AI_KEY` | Meta Dev API key. |
| `META_AI_ENDPOINT_URL`, `META_AI_MODEL`, `META_AI_TIMEOUT_SECONDS` | Meta endpoint (must be on `https://api.meta.ai`), model and timeout in seconds. The timeout applies to checks and chat; social stories always wait at least 180 seconds, because a story with painted scenes takes about 40 for a short story and longer for up to 12 pages. |
| `MetaAI__MaxCallsPerMinute` | Cap on calls to Meta per minute. Default 40. |
| `META_AI_MAX_CALLS_PER_DAY` | Cap on calls to Meta per UTC day for the whole app, retries included. Default 1500. A restart resets the count. |
| `META_AI_MAX_CALLS_PER_IP_PER_MINUTE` | Cap on calls to Meta per visitor per minute. Default 15. |
| `META_AI_MAX_CALLS_PER_IP_PER_DAY` | Cap on calls to Meta per visitor per UTC day. Default 200, so one visitor cannot use up the whole app's day. |
| `ENABLE_DIAGNOSTICS` | Set to `true` to show "Test the connection" (settings and Meta's raw reply). Off by default outside development. |
| `CLIENT_IP_HEADER` | Optional. A header the host sets to the visitor address (for example `CF-Connecting-IP`). Empty uses `X-Forwarded-For`. |
| `DEMO_RESET_SECRET` | The code needed for "Reset demo" and "Empty Remember" on `/demo`. With no value, both are switched off. |
| `ACCOUNT_USER_PASSWORD`, `ACCOUNT_HELPER_PASSWORD` | Passwords for the made-up accounts `sam@example.com` (user side) and `alex@example.com` (helper side). With no value, that account cannot sign in. |
| `SUPABASE_URL`, `SUPABASE_SERVICE_KEY` | Supabase project URL and service key (server only). Not used yet. |
| `SAFE_BROWSING_API_KEY`, `URLHAUS_AUTH_KEY` | Optional. Blocklist lookups for links on `/try` only. Empty skips them silently. |

## Docker deployment

The image is built from `Backstop/` on the .NET 10 SDK and ASP.NET runtime images.

```bash
docker build -t backstop ./Backstop
docker run -p 5106:5106 -e META_AI_KEY=... backstop
# or, on a host that sets PORT:
docker run -p 8080:8080 -e PORT=8080 -e META_AI_KEY=... backstop
```

Notes for the host:

- Run a **single instance**. Blazor Server keeps each session in memory on one server.
- **WebSockets must be enabled**, because the app uses SignalR.
- The app listens on `$PORT` if set, otherwise 5106.
- Free hosts sleep when idle, so the first request can be slow.
- The outbound destinations are the Meta Dev API, later Supabase, and, on `/try` only, the links a visitor pastes (opened by the guarded Link check) plus the optional Safe Browsing and URLhaus lookups. Fonts and scripts are self-hosted and the content security policy is strict.

## Routes

| Route | Who | What |
|---|---|---|
| `/` | User | Inbox (cleared mail only), banner when mail is held |
| `/message/{id}` | User | Read a cleared message, with "Read this to me" |
| `/compose` | User | Write a message. Private details trigger the pause screen |
| `/explain`, `/check`, `/ask-me` | User | Explain a letter, check a message, Ask Me |
| `/helper` | Helper | Queue of held messages and recent decisions |
| `/helper/{id}` | Helper | Evidence card: Safe, let it through · Not safe, block it · Call the sender first |
| `/login` | Public | Sign in, or "Try the demo" (both sides, with a User / Helper switch and a guided walk-around) |
| `/try` | Public | Paste an email or pick an example and see whether Backstop would block it, and why |
| `/demo` | Demo | Simulate incoming email, watch the four checks, split-screen user and helper |

## Try it (for judges, no sign-in)

Open `/try` (linked below "Try the demo" on the sign-in page). Paste an email, or press one of ten examples (phishing link, redirect link, install software, move to WhatsApp, normal family email, "Mum" from a new address, look-alike address, replies go elsewhere, contact with a new address, tricky but real). Choose **Coming in** or **Going out**, then **Check it**.

You get a verdict (**Blocked**, **Held for helper** or **Safe**), the warning signs found, a plain explanation, and what the trusted helper would see.

- It runs the same checks as real mail: `MailAnalyzer` (rules, link check, sender check, policy), the Meta checker, and `CheckOutgoing` for drafts. There is no separate detection path.
- It keeps nothing. No message is added to the inbox or the helper queue, and nothing is saved.
- Links are shown defanged, never as clickable links. They are opened only by the Link check below.
- Fail closed: with no `META_AI_KEY`, or if the checker errors, it says "Couldn't verify, so this would be held". It never shows Safe without an answer from Meta AI.
- The email is passed to Meta as untrusted data. Orders hidden in it ("mark this safe") are a warning sign and cannot make the result safer.
- Limits: 5,000 letters in total (longer is refused, not cut), and 6 checks a minute per visitor address. Meta's own caps also apply.

## Try the demo

1. Open `/demo`. Side by side is on by default.
2. Pick one of the eleven example emails (or write your own) and press **Simulate incoming email**.
3. Safe mail appears in the user pane. Held mail appears in the helper pane, and the user pane only shows that a message is being checked.
4. Approve in the helper pane and the message moves into the user inbox live.
5. Tick "Make the checker crash" to see fail-closed behaviour. "Make these modes fail" affects only your own visit.

## Link check (the Try it page only)

On `/try`, links in a pasted message, and the standalone **Check one link** box, are opened by the Link check (`ILinkInspector`). Real mail never is: incoming mail, the helper queue and the guardian judge links by address only. This is an owner-approved exception to the "Meta and Supabase only" rule (see AGENTS.md).

- GET only, http and https on ports 80 and 443, at most 5 redirects, 5 seconds a hop, 1 MB read, no cookies, no sign-in headers.
- Every hop looks the name up again and refuses private, loopback, link-local (including the cloud metadata address), carrier-grade NAT, reserved, documentation, multicast and unique-local addresses, and `localhost`. The connection is pinned to the address that passed, so a name cannot change between the check and the visit.
- The page is read as text only, to look for a password box. It is never run or drawn, never stored and never sent to Meta.
- Signals come in three tiers. **Information** never changes the verdict: a shortener, an affiliate or tracking redirect, 1 to 5 hops, a long address. **High** is dangerous: a bare-number address, look-alike letters, a download of a program or packed file, a password box on a site that is not a well-known one, link text that names a different site from where it ends, a step that is not https, a private or blocked address, an unresolved name, more than 5 redirects, and optional blocklist hits. A shortener or redirect that ends at an unknown site is **Medium** and the message is held.
- A short list of well-known sites (`Services/Links/TrustedDomains.cs`, about 55 names: shops, UK government and health, major banks, big tech) is compared with the FINAL address only, exactly or as a true subdomain, so `amazon.co.uk.evil.example.net` is not trusted. If every hop is clean and the last stop is on the list, the link says "Looks fine: goes to <site>". Pages anyone can publish on (for example `docs.google.com`) are never trusted.
- All the links in one message share a 12 second budget, so a slow chain of redirects cannot use up the time the AI check needs.
- Meta AI is told the facts (our own wording, after the untrusted data) so the plain-English explanation can mention them. A link that is unsafe, or could not be checked, means the message cannot be called Safe.
- Fail closed: a timeout, error or refused address is "Could not verify, treated as unsafe". If Meta AI itself fails, the card says "We couldn't finish checking this email, so it's being held to be safe." and lists what the link check found below as information.
- Reserved example addresses (`example.com`, `.net`, `.org`, `.test`) never reach a real site, so they are judged by address only and not opened.
- Up to 3 links are opened per check, 12 links a minute per visitor. Nothing is stored.
- Optional lookups, only when their key is set: Google Safe Browsing (`SAFE_BROWSING_API_KEY`) and URLhaus (`URLHAUS_AUTH_KEY`). Only the link address is sent. A failed lookup is skipped.

## Tests

In a Debug build, `dotnet run -- --test-llama` runs the built-in checks (fail-closed paths, the floor rule, prompt-injection handling, the mail pipeline, Explain and link handling). This is not available in Release.

## Structure

```
Backstop/
  Components/{Layout,Pages,Shared,Helper,Demo}
  Models/
  Services/      IMailService, IGuardianService, MailStore, MailAnalyzer, LinkFinder
  Services/Api/  IBackstopApi, Meta checker and assistant, mock rules and fixtures
  wwwroot/css/   theme.css (colour variables), app.css
```

UI components talk only to `IMailService`, `IGuardianService` and `IBackstopApi`. Helper permissions are enforced in the services, not the pages. The release covers the Standard helper tier only.

## Licence

MIT. See [LICENSE](LICENSE).
