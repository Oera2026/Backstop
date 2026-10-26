---
name: Backstop
description: A safe email client drawn as an easy-read page: one sentence, one picture, one button.
colors:
  navy: "#0B2A6F"
  navy-deep: "#061B4D"
  ink: "#0B1B44"
  ink-soft: "#31406B"
  marker-yellow: "#FFE14D"
  page-white: "#FFFFFF"
  band-blue: "#E2EAFF"
  mist-blue: "#F0F4FF"
  rule-blue: "#B8C6EE"
  safe-green: "#0A6B3A"
  safe-green-tint: "#D3F0DF"
  waiting-orange: "#8A3F00"
  waiting-orange-tint: "#FFE2BD"
  blocked-red: "#A3121F"
  blocked-red-deep: "#7D0C17"
  blocked-red-tint: "#FFD9DC"
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
  md: "12px"
  lg: "16px"
spacing:
  sm: "0.75rem"
  md: "1rem"
  lg: "1.5rem"
components:
  button-primary:
    backgroundColor: "{colors.navy}"
    textColor: "{colors.page-white}"
    rounded: "{rounded.md}"
    height: "64px"
    padding: "0.6rem 2rem"
  button-primary-hover:
    backgroundColor: "{colors.navy-deep}"
  button-quiet:
    backgroundColor: "{colors.page-white}"
    textColor: "{colors.navy}"
    rounded: "{rounded.md}"
    height: "56px"
  button-block:
    backgroundColor: "{colors.blocked-red}"
    textColor: "{colors.page-white}"
    rounded: "{rounded.md}"
    height: "64px"
  card:
    backgroundColor: "{colors.page-white}"
    rounded: "{rounded.lg}"
    padding: "1.25rem"
---

# Design System: Backstop

## Overview

**Creative North Star: "The Easy Read Page"**

Backstop looks like the easy-read documents made for people who find ordinary text hard: one short sentence, one drawn picture, one button. Everything is large, outlined and flat. A deep navy band owns the top of the screen, a pale blue band owns the foot, and a marker-yellow highlighter is the only decoration. The system is built for an anxious reader first and a stressed helper second.

**Key Characteristics:**
- Every box has a 3px navy outline and no shadow.
- Every statement is paired with a picture tile.
- The single decision on a screen is the biggest, heaviest control.
- State is always icon, word and colour together.

## Colors

A committed navy-and-white page with three signal colours, each reserved for one meaning.

### Primary
- **Easy-Read Navy** (#0B2A6F): the header band, the primary button, every outline and link.
- **Highlighter Yellow** (#FFE14D): the marker under each page title, the brand shield, the Helper tag, picture-tile fill and the selected checkbox. Never used for a state.

### Neutral
- **Ink** (#0B1B44) text, **Soft Ink** (#31406B) secondary text, **Page White** (#FFFFFF) ground, **Band Blue** (#E2EAFF) footer and hover, **Mist Blue** (#F0F4FF) reading wells, **Rule Blue** (#B8C6EE) quiet dividers.

### State
- **Signal Green** (#0A6B3A) on its tint: safe. **Waiting Orange** (#8A3F00) on its tint: being checked. **Blocked Red** (#A3121F) on its tint: blocked or scam verdict.

**The One Meaning Rule.** Green, orange and red mean safe, waiting and blocked and nothing else; each always has an icon and a word beside it.

## Typography

**Display and Body Font:** Atkinson Hyperlegible Next (self-hosted, with system-ui fallback)

**Character:** A typeface drawn by the Braille Institute for low vision, with letterforms that cannot be confused. Weight carries hierarchy: 800 for titles and the one action, 700 for badges and subjects, 400 for reading text.

### Hierarchy
- **Display** (800, 1.8rem / 36px, 1.15): the page title, with a marker-yellow underline drawn behind each line.
- **Headline** (800, 1.3rem, 1.15): section headings.
- **Body** (400, 1rem / 20px, 1.6): reading text; sentences stay short.
- **Label** (700, 0.9rem / 18px): badges, times, hints. Nothing is smaller than 18px.

**The Thick Marker Rule.** Titles get one yellow highlighter stroke; no other text is highlighted, gradient-filled or colour-washed.

## Layout

A single column, 44rem wide (52rem for the helper, 70rem for the demo), with 1rem gutters. Spacing runs on 0.75, 1 and 1.5rem steps. Rows and cards stack with a 1rem gap. Actions stack full width on phones and sit side by side from 640px. The helper's decision bar is sticky at the foot of the screen.

## Elevation & Depth

Flat. Depth comes only from the 3px outline and from banding, never from shadows or gradients. The helper action bar is separated from the page by a 3px navy top rule.

**The Outline Not Shadow Rule.** If a box needs to stand apart, outline it.

## Shapes

Softly squared: 12px on controls, picture tiles and inputs; 16px on cards, rows and banners. Badges are 8px rectangles, not pills. Picture tiles are square (4.25rem, 3.5rem in rows) with a heavy 2.2px-stroke drawing.

## Components

### Buttons
- **Shape:** 12px corners, 3px navy outline, minimum 56px tall.
- **Primary:** navy fill, white 800-weight text, 64px tall; the one per screen.
- **Quiet:** white fill, navy text and outline.
- **Block:** solid red with a darker red outline; used where blocking is the safe choice.
- **Text:** transparent, underlined navy.
- **Hover / Focus:** hover shifts to Band Blue or a deeper fill; press nudges 2px (off under reduced motion). Focus is a 4px navy ring with an 8px yellow halo, yellow on the navy header.

### Picture tile
Square tile with a 3px outline and a drawn icon. Yellow by default; green, orange or red when it states a status.

### Rows and cards
White, 3px navy outline, 16px corners. A row is picture, words, status badge.

### Banners
A tinted block with a 3px outline in the state colour and a white picture tile on the left.

### Inputs
White, 3px navy outline, 12px corners, 56px minimum. Checkbox is a 35px outlined square that fills yellow with a navy tick.

### Navigation
A navy header band: yellow shield, white wordmark, and plain underlined links on the demo view.

## Do's and Don'ts

### Do:
- **Do** pair every statement with a picture tile and every state with icon, word and colour.
- **Do** make the one decision the biggest, heaviest control on the screen.
- **Do** keep text at 18px or larger and targets at 56px or larger.

### Don't:
- **Don't** use shadows, gradients or glass; outline the box instead.
- **Don't** use yellow to mean a state.
- **Don't** colour a status without an icon and a word.
