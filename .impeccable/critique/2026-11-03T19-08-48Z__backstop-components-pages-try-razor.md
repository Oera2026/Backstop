---
target: Try it page (/try)
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:C:\\Users\\Admin\\Desktop\\Antigravity\\Meta Hackathon\\Backstop\\Components\\Pages\\Try.razor"
target_fingerprint: "sha256:ebe1493a3b35411fa2c6277aa21b20cf1857ee9203aa992989ea9a7045a4520e"
target_path: "C:\\Users\\Admin\\Desktop\\Antigravity\\Meta Hackathon\\Backstop\\Components\\Pages\\Try.razor"
timestamp: 2026-11-03T19-08-48Z
slug: backstop-components-pages-try-razor
---
Method: DEGRADED single-context (sub-agents not used in this session). Detector run; browser overlay not injected. Rendered page measured via Chrome DevTools at 1280 and 360.

# Critique: Try it (/try)

Score 25/40 (Acceptable). Heuristics: 1=2, 2=3, 3=2, 4=3, 5=3, 6=3, 7=2, 8=2, 9=3, 10=2. None n/a.

Design specificity: generic stacked form in the Backstop skin. Result badge (icon + word + colour) is the product-specific part. Detector: [] (clean); it misses structural and copy issues.

## Priority issues
- [P1] Result and loader sit below both forms. Phone: loader ~y1800, result ~y2950; no change visible after tapping a sample; ~3000px back to try again. Fix: result under its own form or replace the form with result + "Try another"; two columns on desktop; loader beside the pressed button. (layout)
- [P1] Too many decisions at once: 10 sample buttons (uneven 60/92px, orphan, 3-line labels at 360px, jargon "Phishing link"), radio pair, 2-4 fields, two primary buttons. Fix: lead with one choice, max 4 plainly named examples, put "Check just a link" behind a quiet control, one primary button. (distill)
- [P1] Result repeats and runs together: unverified headline equals explanation; signal title and detail have no gap ("detailsBank details"); link signals shown twice. Fix: verdict + one reason; fold flagged/sender/links into one "Why" disclosure, each item once, with a separator. (clarify)
- [P2] Dead ends and lost input: no Clear, samples overwrite typed text and auto-submit, 10s wait with no cancel. Fix: "Start again", fill-then-Check, enable samples except while running. (harden)
- [P2] Interface-explaining text ("(not opened)", "Example address: only the address was checked.") and weak disabled-button contrast. Fix: remove captions, stronger disabled style. (polish)

## Persona red flags
Jordan: ten unexplained buttons, no visible change after a tap, abandons within 10s. Sam: title and detail read as one run-on phrase; no status announcement while checking. Casey: primary action mid-page, ~3000px scroll back after a result.

## Minor
Brand link goes to /login; empty right half on desktop; DESIGN.md (navy/yellow) differs from live teal/amber; "Going out" relabels From to To without explanation; 8-10s check time worth confirming.

## Questions
One big "Paste an email" box with 3 example chips? Does a first visit need Reply-to and the link form? What result fits one phone screen?
