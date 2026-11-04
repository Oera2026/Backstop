---
name: Backstop
description: A safe email client drawn as an easy-read page: one sentence, one picture, one button.
colors:
  teal: "#0F4C5C"
  teal-deep: "#0A3440"
  teal-soft: "#E1F1F2"
  ink: "#111827"
  ink-soft: "#374151"
  page-white: "#FFFFFF"
  band-teal: "#D6ECEE"
  mist-teal: "#F3F9F9"
  rule-grey: "#6B7280"
  safe-green: "#166534"
  safe-green-tint: "#DCFCE7"
  checking-amber-ink: "#92400E"
  checking-amber: "#F59E0B"
  checking-amber-tint: "#FEF3C7"
  blocked-red: "#B91C1C"
  blocked-red-fill: "#DC2626"
  blocked-red-deep: "#991B1B"
  blocked-red-tint: "#FEE2E2"
typography:
  display:
    fontFamily: "Atkinson Hyperlegible Next, Atkinson Hyperlegible, system-ui, sans-serif"
    fontSize: "1.8rem"
    fontWeight: 800
    lineHeight: 1.15
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "Atkinson Hyperlegible Next, system-ui, sans-serif"
    fontSize: "1.3rem"
    fontWeight: 800
    lineHeight: 1.15
  body:
    fontFamily: "Atkinson Hyperlegible Next, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Atkinson Hyperlegible Next, system-ui, sans-serif"
    fontSize: "0.9rem"
    fontWeight: 700
rounded:
  md: "8px"
  lg: "12px"
spacing:
  sm: "0.75rem"
  md: "1rem"
  lg: "1.5rem"
components:
  button-primary:
    backgroundColor: "{colors.teal}"
    textColor: "{colors.page-white}"
    rounded: "{rounded.md}"
    height: "64px"
    padding: "0.6rem 2rem"
  button-primary-hover:
    backgroundColor: "{colors.teal-deep}"
  button-quiet:
    backgroundColor: "{colors.page-white}"
    textColor: "{colors.teal}"
    rounded: "{rounded.md}"
    height: "56px"
  button-block:
    backgroundColor: "{colors.blocked-red-fill}"
    textColor: "{colors.page-white}"
    rounded: "{rounded.md}"
    height: "64px"
  badge:
    backgroundColor: "{colors.teal-soft}"
    textColor: "{colors.teal}"
    rounded: "{rounded.md}"
    padding: "0.1rem 0.5rem"
  card:
    backgroundColor: "{colors.page-white}"
    rounded: "{rounded.lg}"
    padding: "1.25rem"
---

# Design System: Backstop

## Overview

**Creative North Star: "The Easy Read Page"**

Backstop looks like the easy-read documents made for people who find ordinary text hard: one short sentence, one drawn picture, one button. Everything is large, outlined and flat. A deep teal band owns the top of the screen, a pale teal band owns the foot, and an amber highlighter under each title is the only decoration. The system is built for an anxious reader first and a stressed helper second.

**Key Characteristics:**
- Every box has a 2px outline and no shadow (4px in High contrast).
- Every statement is paired with a picture tile.
- The single decision on a screen is the biggest, heaviest control.
- State is always icon, word and colour together.

## Colors

A committed teal-and-white page with three signal colours, each reserved for one meaning. Amber only means being checked; brand uses teal.

### Primary
- **Easy-Read Teal** (#0F4C5C): the header band, the primary button, links, the Helper role tag, Online presence, the waiting count and picture-tile icons. Soft teal (#E1F1F2) is its tint.
- **Highlighter Amber** (#F59E0B): the marker under each page title and the logo tile. Beyond the logo and this marker, amber means only being checked.

### Neutral
- **Ink** (#111827) text, **Soft Ink** (#374151) secondary text, **Page White** (#FFFFFF) ground, **Band Teal** (#D6ECEE) footer and hover, **Mist Teal** (#F3F9F9) reading wells, **Rule Grey** (#6B7280) outlines and dividers. Dark mode and High contrast swap the same tokens.

### State
- **Signal Green** (#166534) on its tint: safe. **Checking Amber** (#92400E text, #F59E0B edge) on its tint (#FEF3C7): being checked. **Blocked Red** (#B91C1C text, #DC2626 fill) on its tint: blocked or scam verdict.
- Presence (Online) is neutral teal with a plain dot, never green and never amber.

**The One Meaning Rule.** Green, amber and red mean safe, being checked and blocked and nothing else; each always has an icon and a word beside it. Amber only means being checked; brand uses teal.

## Typography

**Display and Body Font:** Atkinson Hyperlegible Next (self-hosted, with system-ui fallback)

**Character:** A typeface drawn by the Braille Institute for low vision, with letterforms that cannot be confused. Weight carries hierarchy: 800 for titles and the one action, 700 for badges and subjects, 400 for reading text.

### Hierarchy
- **Display** (800, 1.8rem / 36px, 1.15): the page title, with an amber underline drawn behind each line.
- **Headline** (800, 1.3rem, 1.15): section headings.
- **Body** (400, 1rem / 20px, 1.6): reading text; sentences stay short.
- **Label** (700, 0.9rem / 18px): badges, times, hints, tab bar labels (they grow to 1rem at larger text sizes). Nothing is smaller than 18px.

**The Thick Marker Rule.** Titles get one amber highlighter stroke; no other text is highlighted, gradient-filled or colour-washed.

## Layout

A single column, 44rem wide (52rem for the helper, 70rem for the demo), with 1rem gutters. Spacing runs on 0.75, 1 and 1.5rem steps. Rows and cards stack with a 1rem gap. Actions stack full width on phones and sit side by side from 640px. The helper's decision bar is sticky at the foot of the screen.

## Elevation & Depth

Flat. Depth comes only from the 2px outline and from banding, never from shadows or gradients. The helper action bar is separated from the page by a teal top rule.

**The Outline Not Shadow Rule.** If a box needs to stand apart, outline it.

## Shapes

Softly squared: 8px on controls, picture tiles, inputs and badges; 12px on cards, rows and banners. Picture tiles are square (4.25rem, 3.5rem in rows) with a heavy 2.2px-stroke drawing.

## Components

### Buttons
- **Shape:** 8px corners, 2px outline, minimum 56px tall.
- **Primary:** teal fill, white 800-weight text, 64px tall; the one per screen.
- **Quiet:** white fill, teal text and outline.
- **Block:** solid red with a darker red outline; used where blocking is the safe choice.
- **Text:** transparent, underlined teal.
- **Hover / Focus:** hover shifts to Band Teal or a deeper fill; press nudges 2px (off under reduced motion). Motion eases out and never overshoots. Focus is a 4px teal ring with an 8px pale teal halo, a white ring on the teal header.

### Picture tile
Square tile with a 2px outline and a drawn icon. Soft teal with a teal icon by default; green, amber or red when it states a status.

### Badges
Status badge: icon, word and tint, 18px text, 1px outline, 8px corners and only as tall as its text, so it never reads as a button. Presence uses a teal dot instead of an icon.

### Rows and cards
White, 2px outline, 12px corners. A row is picture, words, status badge; the badge wraps under the words on narrow screens.

### Banners
A tinted block in the state colour with a white picture tile on the left.

### Inputs
White, 2px outline, 8px corners, 56px minimum. Checkbox is an outlined square that fills amber with a dark tick.

### Navigation
A teal header band: amber logo tile, white wordmark and a white Helper tag. On phones a four-tab bar sits at the foot (Inbox, Ask Me, Messages, Remember); wide screens get a side bar.

## Do's and Don'ts

### Do:
- **Do** pair every statement with a picture tile and every state with icon, word and colour.
- **Do** make the one decision the biggest, heaviest control on the screen.
- **Do** keep text at 18px or larger and targets at 56px or larger.

### Don't:
- **Don't** use shadows, gradients or glass; outline the box instead.
- **Don't** use amber for anything but being checked; brand uses teal.
- **Don't** colour a status without an icon and a word.
