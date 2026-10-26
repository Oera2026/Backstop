---
target: Backstop whole app
total_score: 26
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 2
target_identity: "file:C:\\Users\\Admin\\Desktop\\Claude Code\\Meta Hackathon\\Backstop\\Components"
timestamp: 2026-10-26T21-28-47Z
slug: backstop-components
---
Method: dual-agent (A: design review, B: detector + browser)

# Backstop critique (whole app: /, /helper, /demo)

Score 26/40 (Acceptable). Heuristics: 1=3, 2=3, 3=2, 4=3, 5=3, 6=3, 7=2, 8=3, 9=3, 10=1.

## Specificity
~70% authored. Product logic shows in UI (held count only, defanged links, compose pause). Visual skin is category-default (healthcare blue, shield, traffic-light). Backstop metaphor not expressed visually. Detector: 0 findings on source; overused-font (Inter) on / and /demo, mandated by AGENTS.md.

## Priority issues
- [P1] Blue Approve is the primary action on flagged mail (EvidenceCard.razor 74-84). Fix: emphasis follows verdict, confirm + undo. /impeccable clarify, shape
- [P1] Decision at bottom of 2214px scroll. Fix: verdict header, sticky action bar, disclosures, end confirmation. /impeccable distill, layout
- [P2] AGENTS.md breaches: 3 buttons on compose pause, #fff in app.css 40/42/76/77, 15-17px text, header/nav targets <56px. /impeccable typeset, polish
- [P2] /demo overflows at 360px (.site-nav 426px). /impeccable adapt
- [P3] No help/orientation; "How sure are we: High" ambiguous. /impeccable onboard, clarify

## Personas
Margaret (anxious elder): no who/when reassurance, tiny text, no queue confirmation. Sam (screen reader): row links blob, no announcement after decide, error-ui dismiss not a button. Raj (stressed helper): unconfirmed irreversible Approve, long scroll, no recommendation, no phone number.

## Minor
Dead Approved/Blocked banner; no times for user; demo iframes push results below fold; Block quiet red vs Approve loud blue.

## Questions
Why does the helper queue sound like a normal inbox? Verdict-first card? Where is the backstop visually? Is blue right for approve?
