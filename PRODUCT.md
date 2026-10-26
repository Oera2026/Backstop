# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

.NET 10 Blazor Server in `Backstop/`, plain CSS, no UI framework (from AGENTS.md; confirmed by the existing project).

## Users

- **The user:** an elderly person or an adult with learning disabilities who reads and writes email. They can be anxious about scams and easily overwhelmed. They need one decision per screen and plain language (reading age about 9).
- **The helper:** a trusted person (family member or carer) who reviews held mail on the user's behalf, often on a phone, sometimes under stress.

## Product Purpose

Backstop is a safe email client. It holds every incoming message until it has been checked. The user never sees mail until it is cleared safe. Anything unclear goes to the helper, who approves or blocks it. Success: the user reads their mail without fear and no scam reaches them.

## Positioning

Fail-closed email: nothing reaches the user until it is cleared, and held mail reveals no sender or subject to the user. A normal mail client cannot truthfully copy this.

## Operating Context

Mock shell only: all people, messages and domains are synthetic (`example.com`, `example.net`, `example.org`, `.test`). No real email, no network calls, no backend. Three views: `/` user, `/helper` helper, `/demo` presenter panel with 7 scenarios and a split-screen of the first two.

## Capabilities and Constraints

- Message states: Received, Scanning, Cleared, Held, Blocked, Failed. Any error or unknown state becomes Held. Blocked is final and invisible to the user.
- Held mail shows the user no sender and no subject, only a count.
- The user can open a message only if it is Cleared.
- Status is always icon + word + colour ("Safe", "Your helper is checking this", "Blocked").
- Helper previews are text only, links are defanged (`hxxp://example[.]com`), no clickable links from message content.
- "Read this to me" on message and explanation screens (browser speech).
- UI talks only to `IMailService` and `IGuardianService`; mock logic stays in `Services/Mock*`.
- Responsive to 360px. PWA manifest.

## Brand Commitments

Name: Backstop. Plain, warm, reassuring voice. The user has explicitly released the earlier style rules (single blue accent, Inter, calm restraint, base 20px), so those are no longer binding. Accessibility and privacy rules above remain binding.

## Evidence on Hand

Synthetic seed messages and 7 demo scenarios in `Backstop/Services/MockData.cs`. No real customers, testimonials or metrics exist; none may be invented.

## Product Principles

1. Safe by default: fail closed, never let a message through on error.
2. One decision per screen; the important thing is unmistakable.
3. Plain words, short sentences.
4. Never rely on colour alone.
5. The helper decides quickly with the evidence in view.

## Accessibility & Inclusion

Cognitive accessibility first (older adults, learning disabilities): large targets (at least 56px), visible focus ring on every interactive element, contrast at least 4.5:1, `prefers-reduced-motion` respected, keyboard navigable, proper landmarks and ARIA, text-to-speech.
