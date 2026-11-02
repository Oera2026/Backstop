# Backstop

A safe email and messaging client for elderly people and adults with learning disabilities.
Backstop holds every incoming message until it is checked. The user never sees held mail. Anything unclear goes to a trusted person (the **helper**), who approves it, blocks it, or asks the user to call the sender first.

> All data is synthetic: made-up people, messages and domains (`example.com`, `example.net`, `example.org`, `.test`). There is no real email provider. No secrets are in this repo.

**Live URL:** https://backstop-301g.onrender.com/home

Built with .NET 10 Blazor Server and plain CSS. No UI framework.

## How it works

Every incoming message goes through four checks, in order: **Rules**, **Meta AI**, **Link check**, **Policy**.

- **Cleared** only when the local checks are clean and Meta AI answered and agreed.
- **Held** for the helper when anything looks wrong, or when a check fails.
- **Blocked** (final, invisible to the user) only for a disguised link together with a strong warning sign, and never when someone may be at risk.
- **Fail closed.** A Meta error, a timeout, an unexpected answer, a missing API key or any exception ends in Held. Nothing is ever cleared on a failure.
- **Local checks are a floor.** The phrase rules and input guard set a minimum. Meta's answer can only make a result stricter, never looser.
- **Links are never opened.** They are judged by their address only, and shown defanged (`hxxp://example[.]com`). Message content never renders as a clickable link.
- **Held mail is private.** The user sees only "N message(s) being checked by your helper", with no sender and no subject.

## Meta API integration

Backstop calls the Meta Dev API (`https://api.meta.ai/v1`, Muse models such as `muse-spark-1.3`) from the server only, for:

- scam checks on messages, conversations and pictures,
- Ask Me,
- safeguarding (spotting when someone may need help).

Text and images being inspected are passed as data, never as instructions. Images are sent only when a picture check needs them. Any endpoint other than `https://api.meta.ai` is refused before a request is sent.

Calls retry twice with backoff, honour `Retry-After`, and pause for 20 seconds if most recent calls fail. A per-minute call cap (default 40, shared by all visitors) holds items instead of calling Meta when it is exceeded. With no `META_AI_KEY`, every check fails closed and Ask Me says it cannot answer.

Explain and questions about a document go to Meta AI. Photos of a document are read by the AI; PDFs cannot be read yet, so Explain asks for a photo or pasted text. Other demo features still use a local mock layer.

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
| `META_AI_ENDPOINT_URL`, `META_AI_MODEL`, `META_AI_TIMEOUT_SECONDS` | Meta endpoint (must be on `https://api.meta.ai`), model and timeout. |
| `MetaAI__MaxCallsPerMinute` | Cap on calls to Meta per minute. Default 40. |
| `META_AI_MAX_CALLS_PER_DAY` | Cap on calls to Meta per UTC day for the whole app. Default 1500. A restart resets the count. |
| `META_AI_MAX_CALLS_PER_IP_PER_MINUTE` | Cap on calls to Meta per visitor per minute. Default 15. |
| `CLIENT_IP_HEADER` | Optional. A header the host sets to the visitor address (for example `CF-Connecting-IP`). Empty uses `X-Forwarded-For`. |
| `DEMO_RESET_SECRET` | The code needed for "Reset demo" on `/demo`. With no value, reset is switched off. |
| `ACCOUNT_USER_PASSWORD`, `ACCOUNT_HELPER_PASSWORD` | Passwords for the made-up accounts `sam@example.com` (user side) and `alex@example.com` (helper side). With no value, that account cannot sign in. |
| `SUPABASE_URL`, `SUPABASE_SERVICE_KEY` | Supabase project URL and service key (server only). Not used yet. |

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
- The only outbound destinations are the Meta Dev API and, later, Supabase. Fonts and scripts are self-hosted and the content security policy is strict.

## Routes

| Route | Who | What |
|---|---|---|
| `/` | User | Inbox (cleared mail only), banner when mail is held |
| `/message/{id}` | User | Read a cleared message, with "Read this to me" |
| `/compose` | User | Write a message. Private details trigger the pause screen |
| `/explain`, `/check`, `/ask-me` | User | Explain a letter, check a message, Ask Me |
| `/helper` | Helper | Queue of held messages and recent decisions |
| `/helper/{id}` | Helper | Evidence card: Approve, Block, Call them first |
| `/login` | Public | Sign in, or "Try the demo" (both sides, with a User / Helper switch and a guided walk-around) |
| `/demo` | Demo | Simulate incoming email, watch the four checks, split-screen user and helper |

## Try it

1. Open `/demo`. Side by side is on by default.
2. Pick one of the seven example emails (or write your own) and press **Simulate incoming email**.
3. Safe mail appears in the user pane. Held mail appears in the helper pane, and the user pane only shows that a message is being checked.
4. Approve in the helper pane and the message moves into the user inbox live.
5. Tick "Make the checker crash" to see fail-closed behaviour.

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
