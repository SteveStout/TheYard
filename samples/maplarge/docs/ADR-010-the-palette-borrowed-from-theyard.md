# ADR: The palette, borrowed from TheYard

Status: accepted, 2026-09-29. Steve's instruction: use the palette from the Yard, and drop the
branding; the sample has its own name.

## Context

The brief says styling is not important and functionality is. That is permission to do nothing,
not an instruction to. A page in a system font on white reads as unfinished; a page that borrows a
finished palette reads as the same hand that built the other site, which is the point of building
it beside one.

## Decision

`wwwroot/css/tokens.css` is TheYard's token sheets (`colors.css`, `sizes.css`, `typography.css`,
`effects.css`) copied with the values unchanged: the Urban slate palette, the teal that fills and
the dark green that draws, the gold that trims, the text greys, the spacing on a 4 px scale, the
type scale, the shadows, and the operator's look (a rule across the top of a panel with a corner
bracket at each end). The contrast figures beside a colour are the source project's measurements;
its `colors.test.ts` holds every pair to WCAG AA, and this project does not re-measure what it did
not change.

The face is IBM Plex Sans, served from `wwwroot/fonts` with its licence beside it, never from a
font service: the one file, the one `@font-face`, preloaded from the head. Every asset the page
loads comes from the page's own origin.

`app.css` writes no colour, size or face of its own; every value is `var(--a-token)`.
`StyleRulesTests` reads it and fails the build on a hex, an `rgb(` or an `hsl(`, and separately on
a token used that `tokens.css` does not declare.

Left behind: the frosted glass, the ribbon ground and the chart marks, which belong to a site with
ribbons and charts. The panel here is solid white with the rule and the brackets, and a page title
carries the gold bar as its underline the way TheYard's do.

The name is The Shed: a small building in the yard with its own sign, the same materials. The
header carries the name and a one-line description, nothing else from the other site.

## What it cost

About a hundred and sixty lines of tokens for a page that uses forty of them. The unused ones are kept so the
sheet is recognisably the same document as its source, and so a later change can reach for a token
that is already there rather than inventing a near match.

## Addendum, 2 October: one stylesheet per part of the page

The single `app.css` grew to more than eight hundred lines, which is the kind of file the 300-line
rule exists to stop, so it is split by part of the page: `base.css`, `header.css`, `page.css`,
`controls.css`, `dialog.css`, `browser.css`, `documents.css`, `phone.css` and `views.css`, linked
from `index.html` in that order after `tokens.css`. Each sheet opens with what it styles and which
view uses it. The rules above now apply to every sheet but `tokens.css`: none writes a colour,
every token it uses is declared, and every sheet closes each brace it opens. `StyleRulesTests`
holds all three, and `FileShapeTests` holds stylesheets to 300 lines.

## Where it sits

This record is about the palette and touches no ring: it sets the colours, sizes and font that the stylesheets read.

## Files

- [`wwwroot/css/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/css/tokens.css): the tokens, and the `@font-face`.
- [`wwwroot/css/`](https://github.com/SteveStout/TheYard/tree/main/samples/maplarge/wwwroot/css): everything else, one sheet per part of the page, in tokens.
- [`wwwroot/fonts/OFL.txt`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/fonts/OFL.txt): the font's licence.
- [TheYard's `src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css): the source of the values.

The panel, with its rule and brackets:

```live path=wwwroot/css/page.css region=page
```
