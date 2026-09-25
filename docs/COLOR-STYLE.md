# Colour and style

How this site looks, and the rules that keep it looking that way. Decided on 2026-09-21 and shipped
as 1.0.0.169 (ADR: The palette, the addendum on teal, dark green and gold; ADR: The glass look). The
live examples are the [inventory](https://theyard.stevenstout.biz/) and the
[Admin tab](https://theyard.stevenstout.biz/?view=admin).

**The idea in one line: teal fills, dark green draws, gold trims, glass panels sit over a soft
watermark, and the grounds stay white and light grey. Every word, number and photograph is fully
solid.**

Every swatch on this page is painted with the token itself, read from `src/styles/tokens.css` when
the page is drawn, and the figures beside it are computed from the same file: the value, then its
measured WCAG contrast on white (a card) and on grey (the page ground). Nothing here is a picture,
so nothing here can go stale. The bars to clear are **4.5** for body text and **3.0** for large
text, lines and other marks.

The rules at the end of this page are held by tests in the gate. A session that never reads this
page still cannot ship a change that breaks one.

## Who it is for

A recruiter or a hiring manager who tapped a link in a post, most likely on a phone, with about ten
seconds. The phone is the primary screen: every picture of a change is read at 375 pixels before it
is read at 1280. The first screen says what this is, who built it and where the resume is, without
a scroll, and every target in it is at least 44 pixels tall.

## Brand colours

```swatches
--color-accent | Teal, the accent
--color-accent-hover | Teal, under a pointer
--color-accent-soft | Teal tint
--color-green-dark | Dark green
--color-teal-deep | Deep teal
--color-teal-header | Header teal
--color-gold | Gold
--color-gold-light | Gold light
--color-brand-mark | The lightning mark
--color-on-accent | Words on a fill
```

### Teal `#006360`, the accent

- **Job:** pressed buttons, links, toggles, focus rings, the store switch, "Load more vehicles", the
  chosen row of the side rail. White on it reads 7.11. As text it reads 7.11 on white and 5.73 on
  grey. Under a pointer it deepens to `#004f4d`, and white on that reads 9.44.
- **Not for:** the top of a tile. It sits 1.09 from the status green `#146c34`, so on a tile it
  would read as "fine".

### Teal tint `#e0ecee`

- **Job:** the soft ground behind an accent thing: a hovered button, the chosen row in the side
  rail. Heading text on it reads 9.30 and the accent 5.89.

### Dark green `#0a3021`

- **Job:** it draws. A card's left edge where the edge is deliberate, the rule under a section
  heading, the top of the header gradient. 14.41 on white and
  11.62 on grey.
- **Not for:** every hairline. Those stay the neutral border. Not a large fill beside deep teal:
  the two are close in darkness and blur together without a rule between them.

### Deep teal `#024345`

- **Job:** the top border of a PLAIN stat tile, a vehicle's title, the chip of an auction still to
  come. White on it reads 11.10. It sits 1.70 from the status green, which is what lets a plain
  tile and a healthy tile read as two different things.
- **Not for:** a chart's line. A line is a series, and the series are below.

### Header teal `#03505a`

- **Job:** the bottom of the header gradient, and nothing else. White on it reads 9.13 and gold
  light 5.06. The teal sampled from the reference picture was too close to the dark green for a
  gradient between them to be seen on a bar fifty pixels tall, so this is one step bluer.

### Gold `#d4aa3a` and gold light `#dcbf57`

- **Job:** trim. The lightning mark, the rule under the header, the site name on the header, the
  underline of a page's title, a short tick at the start of a section's rule, a ring round the
  chosen button, the hairline outside a photograph's frame on the Author page, and the top edge
  of every other headed block there, where the card is white and no amber is near. Gold light
  reads 7.98 on the dark green and 5.06 on the header teal, so it is safe as text anywhere on the
  header.
- **Not for:** anything on white that carries meaning. On white the two read 2.19 and 1.81, under
  3.0: never text, never a line on a chart, never the top of a tile, and never beside the amber
  "worth a look", where gold reads as a warning.
- **How much gold was a decision.** Three levels were drawn: a touch (the mark and the header rule
  only), trim (the list above) and gold forward (gold tile tops, section rules and card edges). Trim
  was chosen. Gold forward puts gold beside the amber tile, which is the one place it must not go.

### The header gradient

```swatches
--gradient-header | The header, on a phone and on the side rail's brand block
```

`linear-gradient(180deg, ...)` from the dark green to the header teal, **top to bottom**, because
left to right could not be seen, with a 3 pixel gold light rule under it. White text reads 14.41 at
its top and 9.13 at its bottom, so text is safe anywhere on it. It is ONE token,
`--gradient-header`, used by every header bar: the phone's header and the side rail's brand block.
The rule under a section heading is 2 pixels and too thin for a vertical gradient to show, so it is
solid dark green with the short gold tick.

## Grounds and text

Unchanged by the new look, on purpose: "I loved the background of white and light grey."

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

- Body text `#524b48` reads 8.54 on white and 6.89 on grey. It was the printed `#5e5653` until
  25 September, when it read under 4.5 over the 30 per cent glass on a ribbon's teal stop and
  deepened a step in the same hue, which also puts it back above the secondary grey. Headings and the big numbers on tiles
  are `#3f3a37`, 11.22 on white and 9.05 on grey.
- **The quiet colour, `#4a4e57`, is one grey for every secondary word, and only ever used inside
  a panel.** Muted and faint were two slate greys a step apart until the
  tweaks pass of 25 September, when the glass went to 30 per cent white and the grey deepened
  until it held 4.5 over the thinner glass where it lies straight over a ribbon's teal or gold
  stop; faint and the rail's muted grey fold into it. Through a panel over the watermark at its
  worst it reads 6.51.

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
- **A chart series never wears one.** A slow-requests line drawn in the warning colour read as an
  alarm to somebody scanning the page, which is the defect that started this page (ADR: The Admin
  tab, as a product, the addendum on the traffic card in plain words).
- **The one line that may:** server errors on "Did anything fail?", because a server error is a
  state. It is something wrong whenever it is above zero.

## Chart colours

A fixed order, never shuffled, so a colour means the same place on every chart.

```swatches
--color-series-1 | First series
--color-series-2 | Second series
--color-series-3 | Third series, and "turned away"
--color-store-sql | The relational store
--color-store-cosmos | The document store
--color-who-people | People, on the activity card
--color-who-scanners | Scanners and crawlers, on the activity card
--color-who-self | The site's own reads, on the activity card
```

1. Teal `#13928b`, the Mark VII teal below.
2. Gold `#a57c1d`, the Mark VII gold. The two are close in lightness and far apart in hue, so they
   are told apart by hue, and a chart that draws both always carries a legend naming them.
3. Neutral grey `#7b7f8a`, 4.00 on white: a third series, or a request the site turned away, which
   is the visitor's and says nothing about the site.

The series are written as the Mark VII tokens and the printed slate gray (`var()`), not as values
of their own; the dark green and the bright teal they were until the styling pass of 25 September
had drifted from what the charts drew since the tweaks pass.

**Who the traffic was** has its own three, on the Site activity card only, stacked in one order:
people in teal `#0a8f85` (3.98 on white), scanners and crawlers in amber `#b8800a` (3.43), and
the site's own reads in blue `#5b78c2` (4.28). Chosen as a set and checked for colour vision
together: the closest neighbours stay well apart under every common deficiency. Marks only; each
band's name is written on it in the heading colour, and a legend names all three.

### The Admin tab's charts and gauges (the tweaks pass, Mark VII)

From 25 September every chart and gauge on the Admin tab is drawn in one instrument grammar, from
Steve's flight-panel picture: one axis line and graduation ticks in the deep teal, no grid, labels
in the quiet grey in small capitals, two series in teal and gold, a gold marker at the end of a
ring's fill and a gold leader line to the callout on a peak.

```swatches
--color-mark-teal | Mark VII, first series
--color-mark-gold | Mark VII, second series
--color-mark-axis | Mark VII, axes and ticks
--color-mark-marker | Mark VII, the marker, the leader and the bracket ticks
```

- Teal `#13928b` reads 3.81 on white and 3.07 on grey, gold `#a57c1d` 3.82 and 3.08: marks,
  never text. Both went a step deeper in the styling pass, where the series test found them just
  under 3.0 on the page ground. They are told
  apart by hue rather than by light, so two series always carry a legend.
- The gauges' tracks are the deep teal faint (`--color-mark-track`, `--color-mark-bar-track`),
  table rules and the rail's current row the same teal fainter still (`--color-mark-rule`,
  `--color-mark-tint`).
- The status colours stay for states and never fill a gauge. A gauge's fill is the deep teal,
  with white inside it once it passes 40 per cent, or the gold for request units, which carries no
  text: no ink clears 4.5 on it, so its reading is printed under the track.

Labels, numbers and legends are in the text colours, never in a series' colour.

**The two stores keep their own pair for now**, slate blue `#536786` and brown `#8a6a4f`, on the
comparison cards, the proof's bars and the visitors graph. They mean "the relational store" and
"the document store" in five drawings and in the records that show them, and moving them on the
page and not in the drawings would be a half move. They move together, in a ship of their own.

## The glass look

- **A soft watermark** fixed behind the page: ONE inline SVG at a tenth of the strength of its
  ink: dotted rows in the Mark VII teal, and nothing else since 25 September. No image
  request and no animation. A page that asked for less transparency, a page in forced colours and
  a printed page get none.
- **Panels are slightly see-through**: white at about two thirds with a blur behind, a hairline
  teal border, a soft teal shadow, a 10 pixel radius. The see-through is in the BACKGROUND COLOUR
  and never in an `opacity` on the container, so nothing inside a panel is ever faded. Where a
  blur is not supported, where the reader asked for reduced transparency, and in forced colours, a
  panel is solid white.
- **The blur is spent where it is cheap**, because a phone pays for it by the pixel: tiles, small
  cards, the window buttons, the filter bar, the intro strip, a vehicle's page, the store bar. The
  Admin tab's wide cards and the hundred vehicle cards are see-through without it. The side rail
  and the documents' dialog are solid.
- **Words are measured against the worst thing behind them**, the watermark's darkest stroke, and
  not against plain white.
- **A ring gauge is honest or absent.** A ring is a share of a known whole: 5 of 5 checks, 118 of
  118 pages, memory against its limit. Milliseconds and request units have no whole and get no
  ring.

## Background

Approved on 2026-09-21 ("that is the one") and shipped as 1.0.0.175 (ADR: The glass look, the
addendum on the ribbon ground). **The background is code, never an image**: a gradient on the page
and one inline SVG, so it costs no request.

- **The ground** runs left to right from green-grey `#dcebe7` through `#f3f7f6` to white, in place
  of the flat grey behind the panels. It is one token, `--gradient-ground`, on the body. The page
  grey stays a token for the few surfaces that are drawn in it, and it is what a page gets where
  the gradient is not drawn.
- **The ribbons** sweep in an S-curve down the left of the content, teal and gold, from the right
  edge of the side rail: the rail's width token when it is docked, its collapsed width when it is
  collapsed, and the screen's edge on a phone, where the rail is the drawer and the ribbons stand
  back a little. Highlight strands in pale gold, two soft star flares and seventy sparks sit on
  them. The faint watermark stays in front of them.
- **Nothing moves** (Steve, 21 September: a minimal site that looks good): the drawing is painted
  once, placed and centred by the stylesheet alone, and costs nothing after the first paint.
  Asked for less transparency, forced
  colours or a printed page, and there are no ribbons, as there is no watermark.
- **Panels, words and pictures are untouched** and fully solid. Every word is measured against the
  ground's darkest stop as well as the page grey.

```swatches
--gradient-ground | The ground, left to right
--color-ground-left | Ground, left
--color-ground-mid | Ground, middle
--color-ground-right | Ground, right
--color-ribbon-gold | Ribbon gold
--color-ribbon-gold-soft | Ribbon gold, soft
--color-ribbon-teal | Ribbon teal
--color-ribbon-teal-light | Ribbon teal, light
--color-ribbon-green | Ribbon dark green
--color-ribbon-shine | Highlight gold
--color-ribbon-shine-pale | Highlight, pale end
--color-ribbon-shine-white | Highlight, white end
--color-ribbon-flare | Flare and spark centre
--color-ribbon-spark | Spark gold
--color-ribbon-star | A flare's arms
```

## The sidebar and the code theme

The side rail's own tokens, light since the record on the sidebar, and the colours code is read in
inside a document (ADR: Code that reads like code). Both are held to AA by the same test.

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

## How the sheet is built

`src/styles/tokens.css` is the only file that writes a value. Every other stylesheet and component
takes its colours, sizes, weights, corners, tracking and layers from it, and the gate holds that.

**Three tiers, top to bottom.**

| Tier | What it holds | Written as |
| --- | --- | --- |
| The palette | Raw values: the printed Urban slate, the ribbon ground, teal and gold, the code theme, the status set | A hex, once |
| The roles | What a value is for: `--color-text`, `--color-accent`, `--glass-bg`, `--shadow-md` | A hex where the role owns the colour, `var()` where it borrows one |
| The components | The side rail's sheet, the Mark VII marks, the operator's rule and ring | `var()` of a role, or a tint of one by `color-mix` |

**A value is written once.** A token that repeats another's colour is written as that token, so
the rail's text is `var(--color-text)` and follows the body when it moves (it had kept the old
brown after the body deepened). A see-through tint is mixed from its token,
`color-mix(in srgb, var(--color-teal-deep) 15%, transparent)`, never copied as an `rgba` of the
token's channels. The swatches on this page, the contrast tests and the gate all read through
`var()` to the value.

**One width scale.** A media query cannot read a custom property, so the scale lives in
`src/lib/breakpoints.ts`, and every media query and every image's `sizes` is written from it.

| Step | What starts there |
| --- | --- |
| 480 | A phone held upright, past a small phone |
| 640 | Past a phone: a document stops filling the screen, pills drop to their desk height |
| 768 | A tablet: the Admin strip four across, the tables loosen |
| 1024 | A desk: the site's rail docks, a vehicle takes two columns |
| 1280 | A wide desk: the Admin rail beside the card, a pinned card gets its own column |
| 1440 | The widest: the hour beside its card, the store bar's whole sentence |

Above a step a sheet writes `(min-width: 640px)`, under it `(max-width: 639.98px)`, so a width
under zoom lands on one side and never between the two. A component asks for a width only through
the constants in that file. A container query measures its own box and keeps its own widths.

**Sizes, weights, corners, tracking and layers are tokens too.** Type from `--text-*`, writing
inside a chart from `--chart-text-*`, a ring's reading from `--ring-text-*`, corners from
`--radius-*`, the two trackings (`--readout-tracking` for small capitals, `--title-tracking` for a
display title), and the stacking order from `--layer-*`. A share of the size around it (`0.9em`),
a zero, a circle's `50%` and `inherit` are not design values and pass.

## The rules, short

1. Teal fills, dark green draws, gold trims. Text colours do not change; the ground is the ribbon
   ground, and it is code, never an image.
2. Status colours mean a state. Never decoration, never a chart series, server errors excepted.
3. Gold never on white as text or data, never on a chart or a tile, never beside amber.
4. A plain tile is deep teal. Only a healthy tile is green.
5. Every colour is a token in `src/styles/tokens.css` with its measured contrast recorded. No raw
   colour in a component.
6. Text and images are always fully opaque.
7. Contrast is tested in the gate, not eyeballed.
8. A value is written once, in the token sheet; a tint is mixed from its token.
9. One width scale, six steps, in `src/lib/breakpoints.ts`.

## What holds them

Each of these fails with a sentence that says what to do, and each is a row in the table of rules
a change has to pass (ADR: The rules a change has to pass).

| Rule | What holds it |
| --- | --- |
| No raw colour, hex, `rgb(` or `hsl(`, in any stylesheet or component outside the token sheet; an exception is on a list with its reason | StyleRulesTests |
| Every hex on this page is the value of a token, and every colour token is on this page | StyleRulesTests |
| Every contrast figure this page states is the figure the tokens give, and clears the bar it needs | StyleRulesTests, and `tokens.test.ts` for every pairing the site makes |
| No chart series is a status colour's value, and a chart's line takes a status tone only for server errors | StyleRulesTests, and the browser suite reads every line's stroke on the traffic card |
| The gold tokens are used only by the header, the brand mark and the named trim | StyleRulesTests |
| `--gradient-header` is defined once and every header bar uses it | StyleRulesTests |
| A colour is written once in the token sheet, a token that repeats one is written as it, and a tint is mixed from its token | StyleRulesTests |
| Every width a page asks about is a step on the one scale, and a component asks only through `src/lib/breakpoints.ts` | StyleRulesTests |
| Every size, weight, corner, tracking and layer a stylesheet writes comes from the token sheet | StyleRulesTests |
| Nothing that holds a word or an image is drawn at less than full strength, and a quiet word is never on the bare ground | `glass.spec.ts`, on the inventory, a vehicle's page and the Admin tab |
