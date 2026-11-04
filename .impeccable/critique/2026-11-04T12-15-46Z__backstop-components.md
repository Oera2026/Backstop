---
target: full UI review of all Backstop screens
total_score: 20
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 5
target_identity: "file:/home/tba/Documents/META Hackathon/Backstop/Backstop/Components"
timestamp: 2026-11-04T12-15-46Z
slug: backstop-components
---
DEGRADED: single-context (skill loaded by Claude, not invoked by the user; sub-agents need an explicit request in this harness). Driven in headless Chromium at 360 and 1440 as user, helper and demo; no META_AI_KEY (fail-closed paths).
Score 20/40 (Acceptable, bottom). H1 2, H2 3, H3 2, H4 1, H5 2, H6 2, H7 2, H8 2, H9 2, H10 2.
Detector: CLI 0 (does not parse .razor); CDP live DOM 5 pages: bounce-easing x6 (motion.css:84,93,97).
P1 Demo guide hijacks navigation: every full load restarts at 1 of 32 and goes to /home (DemoBar.razor:65,74); Save/Restore and backstopTour.save/load are dead code; /demo split iframes both run the guide, user pane pushed to /home; Guide button jumps to /home.
P1 Helper dead end with no AI: Safe replaced by Check again (EvidenceCard.razor:112-123), so nothing can be approved; phone actionbar 281/740px (app.css:232).
P1 Phone overflow at normal size: /ask-me 425px (Send clipped), /try 387px, /helper 364px, /sent 365px; at Extra large /settings 411px, /ask-me 471px.
P1 Compose traps: quick answer replaces typed reply (Compose.razor:197-201, confirmed); Back during countdown sends (:301-308, confirmed); enterkeyhint=next but Enter submits.
P1 Inbox: no held count (HeldCount unused; Messages.razor:22 shows it); 52 undated rows 9023px; "My messages" vs "Messages" tab; "blocked this one" with no referent; held Check drops advice while Ask Me says Do not reply.
P2: ReadAloudButton no Stop/dispose (user side) while ReadAloud with Stop is on helper side; Home only via 40px logo on phone, Ask Me x3 on desktop home; no-key Technical details shown to user; Check delete and Put everything back no confirm; Settings Done always to inbox; login clears email; Ring them = user vs Call them first = sender; 6 approve and 4 block wordings; undo only on held mail decisions (Queue.razor:165, HelperMessages.razor:63,97 instant); Rules score jargon and 3 stacked banners; brand amber == warning amber (--marker == --check-edge).
P3: 3 buttons on Check result; Sam/Margaret, Alex/Priya name mismatch; banned copy (Compose.razor:59, EvidenceCard.razor:95); keep it blocked wording; Your access after Done and lists removed Send as them; Review it below fold; Show more arrows; DESIGN.md stale; 17px tab labels; bounce easing.
