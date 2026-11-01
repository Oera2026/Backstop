# Backstop

A safe email client for elderly people and adults with learning disabilities.
Backstop holds every incoming email until it is checked. The user never sees mail until it is cleared safe.
If a message is not clearly safe, a trusted person (the **helper**) reviews it and approves or blocks it.

> All data is synthetic. There is no real email. Scam checks and Ask Me call Meta's Dev API from the server. No secrets are in the repo.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd Backstop
dotnet run
```

Then open http://localhost:5106.

## Configuration

Set these as environment variables (locally in an untracked `.env`, in production in the host's settings). `.env.example` lists the names with empty values.

| Name | What |
|---|---|
| `PORT` | Port to listen on. Defaults to 5106. |
| `META_AI_KEY` | Meta Dev API key. With no key, every check fails closed (held), and Ask Me says it cannot answer. |
| `META_AI_ENDPOINT_URL`, `META_AI_MODEL`, `META_AI_TIMEOUT_SECONDS` | Meta endpoint (must be `https://api.meta.ai`), model and timeout. |
| `MetaAI:MaxCallsPerMinute` | Cap on calls to Meta per minute, shared by all visitors. Default 40. Over the cap, items are held and Ask Me shows a "busy" message. Set as the environment variable `MetaAI__MaxCallsPerMinute` (double underscore). |

Calls to Meta retry twice with backoff, honour `Retry-After`, and pause for 20 seconds if most recent calls fail. Any failure ends in Held, never Cleared. Errors show a plain message and a closed "Technical details" box.

## Routes

| Route | Who | What |
|---|---|---|
| `/` | User | Inbox (cleared mail only), banner when mail is held |
| `/message/{id}` | User | Read a cleared message, with "Read this to me" |
| `/compose` | User | Write a message. Private details trigger the pause screen |
| `/helper` | Helper | Queue of held messages and recent decisions |
| `/helper/{id}` | Helper | Evidence card: Approve, Block, Call them first |
| `/demo` | Demo | Simulate an incoming email, watch the 4-tier pipeline, split-screen User and Helper |

## Try it

1. Open `/demo`. Side by side is on by default.
2. Pick a scenario and press **Simulate incoming email**.
3. Safe mail appears in the user pane. Held mail appears in the helper pane, and the user pane only shows "1 message is being checked by your helper."
4. Approve in the helper pane and the message moves into the user inbox live.
5. Tick "Make the checker crash" to see fail-closed behaviour: any error means Held.

## Structure

```
Backstop/
  Components/{Layout,Pages,Shared,Helper,Demo}
  Models/        MailMessage, Scenario, enums
  Services/      IMailService, IGuardianService, MockData, MailStore (shared in-memory state)
  wwwroot/css/   theme.css (variables), app.css
```

To plug in a real backend, implement `IMailService` and `IGuardianService` and change the registrations in `Program.cs`.

## Licence

MIT. See [LICENSE](LICENSE).
