# Backstop

A safe email client for elderly people and adults with learning disabilities.
Backstop holds every incoming email until it is checked. The user never sees mail until it is cleared safe.
If a message is not clearly safe, a trusted person (the **helper**) reviews it and approves or blocks it.

> This is a **front-end shell**. All data is synthetic and hardcoded. There is no backend, no real email, no API calls and no secrets.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd Backstop
dotnet run
```

Then open http://localhost:5106.

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
