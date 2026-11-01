# Helper permissions and risky-mail alerts: plan

Synthetic data only. No new network calls (AGENTS.md rules 1 and 3). Everything below runs on the in-memory mock store.

## 1. Goals

1. A helper is told as soon as a risky email arrives.
2. At account setup the user chooses how much their helper can see and do.
3. A higher tier gives more power, the top tier is called **Administrator**.
4. The user can see and change the choice at any time.

## 2. Risky-mail alert

**Trigger.** A message ends in `Held` or auto-`Blocked`, or a Check result comes back `BeCareful` or `DontReply`. The trigger sits in the pipeline where state changes (`MockMailService.ReceiveAsync`, around the `MessageState.Held` assignments), so the "any error becomes Held" rule also raises an alert.

**Alert contents.** Who it is for, a severity (held, blocked, scam verdict), the message id, a time. Sender and subject are shown to the helper only. The user still sees only "N messages being checked".

**Delivery (mock).**
- Extend `HelperNotice` (`Models/Extras.cs`) with `Kind` (`RiskyMail`, `FirstTimeRecipient`, later kinds), `Severity`, `MessageId`.
- `MailStore` already keeps notices (`OpenNotices`, `AddSent`); add `AddNotice`.
- Helper home shows open alerts at the top. Tapping one opens the evidence card.
- A small `INotificationChannel` interface with one mock implementation (in-app list plus a tab-title count). Email, SMS or push are later implementations behind the same interface, and need a decision on rule 3 before anyone adds them.

**Never switched off.** `RiskAlerts` and `ReviewHeldMail` are the floor in every tier, including Minimal and Custom. A user can't choose "my helper hears nothing", because held mail would then sit forever.

## 3. Tiers

| Capability | Minimal | Standard | Custom | Administrator |
|---|---|---|---|---|
| Risk alerts (floor) | yes | yes | yes | yes |
| Review held mail (floor) | yes | yes | yes | yes |
| See check requests | no | yes | pick | yes |
| See shared memories | no | yes | pick | yes |
| See activity summary | no | yes | pick | yes |
| See cleared mail | no | no | pick | yes |
| See full activity | no | no | pick | yes |
| Send email as the user | no | no | pick | yes |

Defined in code in `Models/Permissions.cs` (`HelperCapability`, `HelperTier`, `HelperPermissions`). The default is Minimal. An unknown tier gets the floor only.

**Administrator** can send email from the user's account and see their full activity. Safeguards:
- A message sent by an administrator is marked in the user's Sent list ("Sent by Priya") and in the helper's activity log.
- Every administrator action is written to the activity log; the user can read it.
- Choosing Administrator needs an extra confirm step, and the user can step down any time without the helper's approval.

## 4. Setup flow

Add a step to `Pages/User/Welcome.razor` (currently 3 steps: text size, colours, ready): **"Who helps you, and how much?"**
- One decision per screen: four large choices (Minimal, Standard, Custom, Administrator), each with a one-line plain description of what the helper can do.
- Custom opens a second screen of simple switches, one per capability.
- Administrator leads to a confirm screen.
- Reachable later from Settings ("Helper access"), reusing the same components.
- Wording stays plain (reading age 9) and has no reassurance captions (AGENTS.md "No self-explaining interface text").

## 5. Enforcement

- New `IPermissionService` (`Services/Interfaces.cs`) with `Get()`, `Set()`, `Allows(HelperCapability)`, `Changed`. `MockPermissionService` over `MailStore`. UI talks only to services (rule 7).
- `IGuardianService` methods check `Allows(...)` and return empty or null when a capability is missing (fail closed). Pages hide what isn't allowed, but the service is the real gate.
- `SendAsUser` is a new guardian method that calls the same send path as the user, with the sender marked.
- Navigation in `HelperLayout` is built from the granted capabilities, so a Minimal helper sees only Alerts and Held mail.
- Tier changes raise `Changed`, so open helper screens update live (same mechanism as `LiveComponentBase`).

## 6. Build order

1. **Foundation** (done): `Models/Permissions.cs`.
2. **Alerts:** extend `HelperNotice`, raise on Held/Blocked/risky verdicts, show on helper home, add `INotificationChannel` plus mock. Demo scenarios 1-7 should each show the right alert.
3. **Permission service:** `IPermissionService` and mock, wire into DI, enforce in `MockGuardianService`.
4. **Setup flow:** new Welcome step, Custom screen, Administrator confirm, Settings entry.
5. **Administrator powers:** send as user, full activity view, sent-by marker, audit log.
6. **Helper navigation and screens** filtered by capability.
7. **Checks:** run each tier through all 7 demo scenarios; confirm Minimal can't see cleared mail, only Administrator can send; confirm an error still ends in Held and still alerts; confirm the user never sees held sender or subject.

## 7. Open questions

- Should Standard exist, or only Minimal, Custom and Administrator? (Your message said "perhaps some other tiers".)
- Should the helper need to accept the access level, or is it only the user's choice?
- Can an Administrator change the user's tier? Recommended: no, only the user can.
- Which alert channels matter first beyond the in-app list: email, SMS, push? Each is a new network call and needs sign-off against rule 3.
