# Colour and style

**Every colour, size and width on this site is a design token, kept in four files named for what they control:** [`colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css), [`sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css), [`typography.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/typography.css) and [`effects.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/effects.css). A design token is a named CSS custom property (a CSS variable) holding one design decision. {{live:facts api/TheYard.Tests/StyleRulesTests.cs}} tests read those files, every stylesheet and the Style pages on every build, and a change that breaks a rule does not ship. [Read the tests on GitHub](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs).

**The swatches below are live.** Each one is painted with the design token itself, read from
`colors.css` when you open this page, and the contrast figure beside it is computed from the same
file. A figure is never typed on this page. The bars to clear are **4.5** for body
text and **3.0** for large text, lines and other marks.

Teal fills, dark green draws, gold trims. Gold is decoration only: it is never text and never data.
The ground behind every page, and the ribbons on it, have a page of their own:
[Background and ribbon](https://theyard.stevenstout.biz/?doc=background-ribbon).

**2026-09-29, the teal moved bluer (palette F) and the gold became a bar, to match the resume.** Steve
chose both on 29 September, colour values only: the grounds, the ribbons' shapes, the glass, the face and the
layout are as they were. The teals moved to hue 193, blue with enough green to soften the step to the gold:
`--color-accent` `#12677f`, `--color-accent-hover` `#0f576b`, `--color-accent-soft` `#ddebee`,
`--color-green-dark` `#0f4452`, `--color-teal-deep` `#10495a`, `--color-teal-header` `#125568`,
`--color-ribbon-teal` `#308ca6`, `--color-ribbon-teal-light` `#64afc4`, `--color-ribbon-green` `#1d5463`,
`--color-ground-left` `#deeaed`, `--color-mark-teal` `#1f8ead`, `--color-who-people` `#1d90af`,
`--color-header-text-muted` `#d1e1e6` and `--color-store-sql` `#4a6c96`. The golds were cut from the gold bar:
`--color-gold` `#b8923f`, `--color-gold-light` `#e6cb7e`, `--color-ribbon-gold` `#c9a95c`,
`--color-ribbon-shine` `#f0dc94` and `--color-mark-gold` `#9a7728`, with `--color-ribbon-gold-soft` and
`--color-ribbon-spark` now written as the gold light. Three are new: the bar's two outer tones and
`--gradient-gold`, the bar itself, which draws every page title's underline and every stat tile's top rule.
Why each value is what it is, and the three that moved from the brief, is in ADR-016's addendum of the same day.

## Brand colours

```swatches
--color-accent | Teal, the accent
--color-accent-hover | Teal, under a pointer
--color-accent-soft | Teal tint
--color-green-dark | Dark green
--color-teal-deep | Deep teal
--color-teal-header | Header teal
--color-gold | Gold
--color-gold-light | Gold light, the gold bar's highlight
--color-gold-bar-dark | The gold bar, its dark ends
--color-gold-bar-shade | The gold bar, its shade
--color-brand-mark | The lightning mark
--color-on-accent | Words on a fill
```

- **Teal `#12677f`** fills: buttons, links, toggles, focus rings, the chosen row of the side rail.
  White on it reads 6.42, and as text it reads 6.42 on white and 5.18 on grey. Under a pointer it
  deepens to `#0f576b` (white on it 8.09). It is never the top of a tile: it sits 1.01 from the
  status green `#146c34` in lightness, so a teal tile would read as "fine".
- **Teal tint `#ddebee`** is the soft ground behind an accent thing. Heading text on it reads 9.19
  and the accent 5.25.
- **Dark green `#0f4452`** draws: a card's edge, the rule under a section heading, the top of the
  header gradient. 10.67 on white and 8.60 on grey. Since 29 September it is a deep blue-green, the
  name kept so no reference moved.
- **Deep teal `#10495a`** tops a plain stat tile and titles a vehicle. White on it reads 9.89. It
  sits 1.52 from the status green, which is what keeps a plain tile and a healthy tile apart.
- **Header teal `#125568`** is the bottom of the header gradient and nothing else. White on it
  reads 8.32.
- **Gold `#b8923f` and gold light `#e6cb7e`** are trim, cut from the gold bar: gold is its mid
  tone and gold light its highlight. They draw the lightning mark, the rule under the header, the
  ring round a chosen button and a photo's frame. Gold light reads 6.71 on the dark green and 5.23
  on the header teal, so it is safe as text on the header. On white the two read 2.91 and 1.59, so
  gold is never text, never data and never beside the amber "worth a look", where it would read as
  a warning.
- **The gold bar** is a page title's underline and a stat tile's top rule: dark at both ends
  (`#7d582e`), the highlight in the middle, the ribbon gold and a shade (`#ad8e57`) either side of
  it. It is drawn with `border-image`, so the rule is the bar; a browser without it draws the flat
  gold.

```swatches
--gradient-header | The header, on a phone and on the side rail's brand block
--gradient-gold | The gold bar, under a page title and on a stat tile's top
```

The header gradient runs top to bottom, dark green to header teal, because left to right could not
be seen on a bar fifty pixels tall. White text reads 10.67 at its top and 8.32 at its bottom. It is
one design token, used by every header bar.

## Grounds and text

```swatches
--color-bg | Page grey
--color-surface | Card white
--color-surface-muted | Muted surface
--color-border | Border
--color-border-strong | Border strong
--color-neutral-soft | Neutral chip
--color-text | Text
--color-text-muted | Text muted
--color-text-faint | Text faint
--color-heading | Heading
```

- Body text `#524b48` reads 8.54 on white and 6.89 on grey. Headings and the big numbers on tiles
  are `#3f3a37`, 11.22 on white and 9.05 on grey.
- **One grey, `#4a4e57`, for every secondary word, and only ever inside a panel.** It is measured
  against the worst thing behind it: the thinnest glass over the darkest ribbon. Through a panel
  over the watermark at its worst it reads 6.53.

## Status colours, reserved

These mean a state and nothing else, always with a word beside the colour ("fine", "worth a look",
"needs attention"), never colour alone.

```swatches
--color-success | Fine
--color-success-soft | Fine, soft ground
--color-warning | Worth a look
--color-warning-soft | Worth a look, soft ground
--color-warning-border | Worth a look, the hairline round a notice
--color-danger | Needs attention
--color-danger-soft | Needs attention, soft ground
--color-live | A live auction's dot
```

- Fine `#146c34` reads 6.51 on white, worth a look `#b45309` 5.02, needs attention `#b91c1c` 6.47.
- **A chart line never wears one.** A slow-requests line drawn in the warning colour read as an
  alarm to somebody scanning the page. The one exception is server errors, because a server error
  is a state.

## Chart colours

A fixed order, never shuffled, so a colour means the same thing on every chart. Labels, numbers and
legends are in the text colours, never in a series' colour.

```swatches
--color-series-1 | First series
--color-series-2 | Second series
--color-series-3 | Third series, and "turned away"
--color-store-sql | The relational store
--color-store-cosmos | The document store
--color-who-people | People, on the activity card
--color-who-scanners | Scanners and crawlers, on the activity card
--color-who-self | The site's own reads, on the activity card
--color-mark-teal | Charts and gauges, teal series
--color-mark-gold | Charts and gauges, gold series
--color-mark-axis | Charts and gauges, axes and ticks
--color-mark-marker | Charts and gauges, the marker at a fill's end
```

1. Teal `#1f8ead`, 3.80 on white and 3.06 on grey.
2. Gold `#9a7728`, 4.16 on white and 3.36 on grey. The two are close in lightness and far apart in
   hue, so a chart that draws both always carries a legend.
3. Neutral grey `#7b7f8a`, 4.00 on white: a third series, or a request the site turned away.

The Admin tab's charts and gauges share one instrument style, modelled on an aircraft panel: one
axis line with ticks in deep teal, no grid, labels in small capitals, and a gold marker where a
gauge's fill ends. The two data stores keep their own pair, slate blue `#4a6c96` and brown
`#8a6a4f`. The activity card stacks who came in three colours chosen as a set that stays apart
under colour blindness: people `#1d90af` (3.71 on white), scanners and crawlers `#b8800a` (3.43),
and the site's own reads `#5b78c2` (4.28).

## The glass

```live path=src/styles/effects.css region=glass-design-tokens
```

```live path=src/styles/panels.css region=op-glass
```

Panels only. A panel is frosted glass: a white tint at 30 percent for a tile (38 on a phone) and
heavier on a reading panel, blurred at 28 pixels, with a hairline of the deep teal at 18 percent
round it and a 10 pixel radius. The see-through part is the background colour, never an `opacity`
on the panel, so nothing inside a panel is ever faded. Where blur is not supported, where the reader
asked for less transparency, and in forced colours, every panel turns solid white. What the glass
sits on is the ground, on [Background and ribbon](https://theyard.stevenstout.biz/?doc=background-ribbon#the-ground).

- **A ring gauge is honest or absent.** A ring shows a share of a known whole: 5 of 5 checks, memory
  against its limit. A reading with no whole, such as milliseconds, gets no ring.

## Side rail and code theme

The rail is white on the ground's darker end. Code takes six ink colours, each deepened until it
reads as ink on the code block's grey. Both are held to WCAG AA by the same test.

```swatches
--color-sheet-bg | Rail ground
--color-sheet-bg-raised | Rail, hovered or chosen row
--color-sheet-text | Rail text
--color-sheet-text-muted | Rail text, muted
--color-sheet-icon | Rail icon
--color-sheet-icon-active | Rail icon, active
--color-sheet-divider-strong | Rail divider, strong
--color-header | Header, flat, under the gradient
--color-header-text | Header text
--color-header-text-muted | Header text, muted
--color-code-text | Code
--color-code-keyword | Code, a keyword
--color-code-type | Code, a type
--color-code-string | Code, a string
--color-code-comment | Code, a comment
--color-code-number | Code, a number
--color-code-meta | Code, an attribute
```

## Sizes and widths

Colour is half of a style guide. The other half is size, and it is held the same way.

**One width scale, six steps.** A media query cannot read a CSS variable, so the steps live in
[`src/lib/breakpoints.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/breakpoints.ts),
and every media query in the site is written on them. Open the
[inventory](https://theyard.stevenstout.biz/?view=inventory) and narrow the window to watch them change the page.

| Step, CSS pixels wide | What starts there |
| --- | --- |
| 480 | A phone held upright |
| 640 | Past a phone: pills drop to desk height |
| 768 | A tablet: the Admin strip goes four across |
| 1024 | A desk: the side rail docks |
| 1280 | A wide desk: the Admin rail sits beside the card |
| 1440 | The widest: the hour beside its card |

```live path=src/lib/breakpoints.ts region=the-four-steps
```

The four a component may import are above; the other two are read only by stylesheets. Above a step a sheet writes `(min-width: 640px)`, under it `(max-width: 639.98px)`, so a width
under zoom always lands on one side.

**Tables stack when their card is too narrow.** Every table on the Admin tab is drawn by one
component, `DataTable`. It measures its own card, not the screen, and when the card cannot give
each column room ([`tableFit.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/tableFit.ts)),
each row becomes a small block of labels and values. See the nine columns of the
[store card](https://theyard.stevenstout.biz/?view=admin&card=store) on a phone.

**Charts are drawn at their real width.** A chart laid out for a desk and shrunk onto a phone
shrinks its words too. Each chart measures the width it is given and lays itself out at that width
(`fitBox`), so its labels keep their size and only the plot narrows. See the
[activity chart](https://theyard.stevenstout.biz/?view=admin&card=activity) on a phone.

**Type, weights, corners, tracking and layers are design tokens too.** Type from `--text-*`, words inside
a chart from `--chart-text-*`, corners from `--radius-*`, the stacking order from `--layer-*`, and a
touch target of 44 pixels on a phone.

**The base styles have their own small file,**
[`src/styles/base.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/base.css): box
sizing, the body's face and ground, headings with no default margin. It sets defaults from the
design tokens and writes no design value of its own.

## How the sheet is built

```live path=src/styles/colors.css region=the-opening
```

The design tokens live in four files, each named for what it controls, and `colors.css` reads top
to bottom in three tiers: the palette (a colour named for what it is: `--palette-white`), the roles
(a colour named for what it does: `--color-accent`), and the components (a colour named for where it
goes: `--color-sheet-icon-active`). A component file may read a role. It may never read the
palette, and it may never write a colour of its own.

| Tier | What it holds |
| --- | --- |
| The palette | Raw values, each written once as a hex |
| The roles | What a value is for: `--color-text`, `--color-accent`, `--glass-bg` |
| The components | The side rail, the chart marks, the panel rule and the ring, each a `var()` of a role |

**A value is written once.** A design token that needs another's colour is written as that one,
`var(--color-text)`, so it follows when the colour moves. A see-through tint is mixed from its design token
with `color-mix()`, never copied as an `rgba`.

**The browser floor.** The tints use `color-mix()`, which every major browser has had since early
2023. An older browser draws those tints as nothing, and the words and grounds, which are plain hex
design tokens, read the same everywhere.

## What holds them

{{live:facts api/TheYard.Tests/StyleRulesTests.cs}} facts in
[`StyleRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs)
run on every build, each a row in ADR-075's table. Each one fails with a sentence that says what to do, so a change that breaks a
rule learns the rule from the failure.

1. **No raw colour.** No stylesheet or component writes a hex, `rgb()` or `hsl()`. Every colour is
   a design token.
2. **The Style pages match the sheet.** Every hex on this page and on Background and ribbon is a
   design token's value, and every colour design token is on one of the two.
3. **Every contrast figure here is real.** Each figure on this page is the one the design tokens give, and
   it clears the bar it needs.
4. **Status colours stay status.** No chart series is a status colour, and a line turns red only for
   server errors.
5. **Gold is trim.** Only the header, the brand mark and the named trim use it.
6. **One header gradient.** It is defined once, and every header bar uses it.
7. **The other checks are still there.** The browser test that nothing a visitor reads is faded, and
   the contrast test for every pairing, both still exist and still run.
8. **One size per control.** Every focus ring, control height and title weight comes from a design token.
9. **One face.** IBM Plex Sans everywhere, with even-width figures set once on the body, and a
   monospaced face only on code.
10. **One width scale.** Every width a page asks about is one of the six steps.
11. **Values from the sheet.** Every size, weight, corner, tracking and layer comes from a design token.
12. **Written once.** No colour is written twice in the sheet, and every tint is mixed from its
    design token.
13. **One panel look.** Every panel, card and tile is the one shared glass with its rule and
    brackets, and every button is a pill or a circle.
14. **The gold bar.** Every page title's underline is drawn as the gold bar, and so is every stat
    tile's top rule.

Two more tests stand behind them.
[`colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts)
measures the contrast of every text and ground pairing the site makes, and
[`glass.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.spec.ts) opens the
site in a real browser and checks that nothing a visitor reads is faded.
