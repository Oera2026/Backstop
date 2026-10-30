---
target: critique (all Backstop screens)
total_score: 23
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
target_identity: "file:C:\\Users\\Admin\\Desktop\\Claude Code\\Meta Hackathon\\Backstop\\Components"
timestamp: 2026-10-27T23-50-23Z
slug: backstop-components
---
Method: dual-agent (A: design review · B: detector + browser overlay)

## Design health score

| # | Heuristic | Score | Key issue |
|---|---|---|---|
| 1 | Visibility of system status | 2 | Inbox never shows held mail (HeldCount unused); "Don't reply" advice disappears once Priya is asked |
| 2 | Match with the real world | 3 | User copy plain; helper copy drifts ("Blocked by checker", "the user") |
| 3 | User control and freedom | 3 | Send/decision undo good; Settings Done always to inbox; Check "Delete it" has no undo |
| 4 | Consistency and standards | 2 | Approve vs Safe – release; helper/Priya/Trusted person; A/A+/A++ vs Normal/Large/Extra large |
| 5 | Error prevention | 2 | Trusted person view in user Settings; "Call them first" reads as calling the scammer |
| 6 | Recognition rather than recall | 3 | Home hidden behind logo on phones; two look-alike boxes on Remember |
| 7 | Flexibility and efficiency | 2 | 54 undated inbox rows; helper queue not ordered by risk |
| 8 | Aesthetic and minimalist design | 2 | Explain result ~27 controls, 9 before content on phone |
| 9 | Error recovery | 2 | ErrorState+retry good; "I'm stuck" lands only in activity log |
| 10 | Help and documentation | 2 | /help covers email only |
| **Total** | | **23/40** | **Acceptable** |

## Design specificity verdict
Visual language is product-specific (easy-read leaflet: navy, 3px outlines, marker titles, picture tiles, Atkinson). Structure is generic: every mode = choice, input, navy button, long result with actions at the bottom. Missed: no visual form for the held item travelling between Margaret and Priya.
Detector: 8 advisory findings, all in Layout/ReconnectModal.razor.css (design-system-color x6 at 30,44,93,100,104,115; design-system-radius x2 at 29,96) — stock Blazor template leftovers. CLI does not scan .razor files (verified), so a clean CLI result does not cover the screens. Overlay on /home, /home (375), check result, explain result, /remember, /helper: 7 border-accent-on-rounded on mark.hl (modes.css:142), judged false positive.

## Priority issues
- [P0] Trusted person view switch in user Settings (Shared/SettingsPanel.razor:29-31) breaks held privacy and fail-closed. Fix: remove; keep in /demo only; real build needs helper sign-in. Command: harden
- [P1] Held items invisible / advice dropped: inbox shows no held count; held Check screen replaces verdict with "You'll see it once it's cleared". Fix: held-count row atop inbox + Home card; keep verdict + "Don't reply or send anything until Priya has looked". Command: clarify
- [P1] Colour meanings overloaded: green tick "Calm" on flagged "my darling" message (RiskThread.razor); orange = waiting + Be careful + Getting worrying + care banner (hand icon, above title). Fix: Calm without tick/green; Be careful/Getting worrying own icon, pale red; care note navy after title. Command: colorize
- [P1] Phone: decision far from evidence — Check "Ask Priya" ~3250px; helper sticky bar 174px covers flags; Explain overview at 900px. Fix: actions under verdict; 64px helper bar; collapse Explain controls. Command: layout
- [P2] Helper screens inconsistent (Approve vs Safe – release, back link on one only, identical orange badges, "Call them first" = sender); stuck requests buried in Recent activity. Fix: shared decision component, "Call Margaret first", sort by risk, "Margaret needs you" section. Command: shape

## Persona red flags
- Jordan: Home only via logo on phones; /help ignores new modes; Explain asks reading level first; two look-alike boxes on Remember.
- Sam: tab labels fixed 14.4px (modes.css:17), tags/time 17px; skip link "Skip to the message" everywhere; no Read aloud on Check verdict, guide or memory.
- Casey: Check primary ~4 screens down; Explain choices fill first screen.
- Margaret: advice vanishes when held; green tick on "my darling"; chemicals warning above washing title; held mail unacknowledged; helper view in her Settings; "I'm stuck" opens below fold without focus.

## Minor observations
- Pills (.tag, .deadline, .chip 999px) vs DESIGN.md 8px badges; yellow drifting to "warning"; .lookhere animates box-shadow.
- Banned self-explaining copy: "(text only, links made safe)", "Guide picture – a guide only", "…so your helper has been told", "It is fine to wait."
- Cross (Blocked) icon on "You cannot reply".
- Data clash Mrs Okafor / Amara Okafor; brand names WhatsApp, Windows in fixtures.
- ReconnectModal.razor.css template hex.

## Questions to consider
- Could "Priya is checking this" be the hero of the inbox?
- Would one "Something is worrying me" entry serve better than five tools?
- Should Priya's screen say "This pretends to be you — call Margaret now"?
