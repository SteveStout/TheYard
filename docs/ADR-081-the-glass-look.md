# ADR: The glass look

Status: accepted, 2026-09-21. Asked for in a few sentences over one morning. On the palette: "I like
the color scheme we have but it feels a little bland." On the surfaces, with a still from a film's
heads-up display as a mood and nothing more: "Can we keep the colours but make the UI a little more
modern?" Then, and this is the specification: "Slightly transparent and modern, maybe some sort of
soft watermark in the background, but all images and text are zero transparency." And on who it is
for: "The goal is to drive recruiter and hiring manager engagement, along with people becoming more
engaged with my website through LinkedIn posts, so this has to look pretty."

## Context

The reader this record is for is not an engineer at a desk. It is a recruiter or a hiring manager
who tapped a link in a post, most likely on a phone, inside an in-app browser, with about ten
seconds. That reader was landing on a white header, a store switch and a column of eight filters.
Nothing on the first screen said what the site is, who built it or where the resume is, and not one
vehicle was in it. The site was correct and it was plain, and to that reader plain reads as
unfinished.

The colours are their own decision and are in the palette's record (ADR: The palette, the addendum
on teal, dark green and gold). This record is the surfaces: what a panel is, what is behind it, and
what the first screen says.

## What was considered

Three ways of taking the heads-up idea were drawn as mock-ups on the site's own Admin tab before any
code was written, and Steve chose with all three in front of him.

| Option | What it is | Why it was or was not taken |
| --- | --- | --- |
| A light readout | White panels on the light grey ground, corner brackets on every panel, ring gauges, small capitals | The brackets are decoration on every panel of every page, and on a phone they are noise at the corners of a card 160 pixels wide |
| A dark deck | The tiles and the charts on dark glass panels, the rest of the page light | He had said of the mock-ups before it, "I loved the background of white and light grey." A dark band across a light page is two sites |
| **Glass, with a watermark** | Panels slightly see-through over one soft drawing fixed behind the page; every word, number and photograph fully solid | His words, nearly verbatim, and the only one of the three whose whole effect survives at 375 pixels |

Nothing was taken from the film still: no layout, no element, no name. What is kept from the genre
is what helps somebody with ten seconds, and each piece had to earn its place against that reader.

| Piece | Kept | Why |
| --- | --- | --- |
| A ring beside a number | On three tiles | A ring is a share of a known whole: checks passing, pages up, memory against its limit. It says "all of them" faster than "5 of 5" does |
| A ring on milliseconds or request units | No | There is no whole. A ring drawn for looks is a gauge that measures nothing |
| Small spaced capitals over a tile | Yes | The question over a tile is a label on an instrument, read after the number and not before it |
| A fine grid and a readout on a chart | Yes | A line with a number under the pointer is a chart somebody can read a value off |
| An index chip on a chart's title | Yes | Three charts on one card, and "the second one" is how people talk about them |
| A status line in the header | No | On a desk this site has no header: the rail carries the brand and the version, and the footer carries the commit. A third place for the same two facts is decoration |
| Animation of any kind | No | It costs frames on the phone this is for, and says nothing |

## Decision

**Panels are glass, the page has one soft watermark behind it, and everything that is read is
solid.**

**The watermark** is ONE inline SVG (`src/components/Watermark.tsx`), fixed behind the page's
content at a tenth of the strength of its inks: concentric rings in the teal, dotted rows in the
lighter teal, and the site's own lightning mark in gold. It is a drawing and not a picture, so it
costs no request, and it does not move, so it costs no frame. It holds no words and no image, which
is why it may be faint. The side rail is solid and sits over it. A page that asked for less
transparency, a page in forced colours and a printed page get no watermark at all.

**A panel** is white at about two thirds, with a hairline teal border, a soft teal shadow and a ten
pixel radius. The see-through is in the BACKGROUND COLOUR and never in an `opacity` on the
container, because an opacity fades everything inside it: the rule is that a panel's words, numbers
and photographs are always at full strength, and `tests/e2e/glass.spec.ts` holds it against the
rendered page by finding every element drawn at less than full strength and failing if one of them
holds a word or an image. Two disabled buttons had said "not yet" with an opacity of 0.6; they say
it with the colours of a thing that is not pressable now.

**The blur is spent where it is cheap.** A backdrop blur is paid for by the pixel on a phone. The
tiles, the small cards, the window buttons, the filter bar and the intro strip carry it. The Admin
tab's wide cards, which are the large ones, are see-through without it. The hundred vehicle cards on
the inventory are solid white and carry neither: a photograph sits in each and there are a hundred
of them. The full-height side rail is solid.

**Three fallbacks, in one place.** The panel's ground is one token, `--glass-bg`. Where
`backdrop-filter` is not supported, where the reader asked for reduced transparency, and in forced
colours, that token is solid. `src/styles/tokens.test.ts` holds that the three blocks are there.

**Words are measured against the worst thing behind them**, not against plain white. The darkest ink
in the watermark is the teal of its rings, and at a tenth of its strength on the page ground it is
`#d2d9da`. Seen through a panel that is `#f0f2f2`, and every text colour the site has clears AA on
it: the faint colour, which is the weakest, reads 5.2. On the BARE ground it is closer: the heading
reads 7.84, the body text 5.01, the accent 4.97, the status green 4.55 and the status red 4.52, all
over 4.5, and the muted and faint colours read 4.21 and 4.08, which is not. So the rule is that **the
two quiet colours are only ever used inside a panel**. The token test holds the arithmetic, and the
browser suite holds the rule against the rendered inventory, a vehicle's page and the Admin tab by
walking up from every quiet word to find the ground it sits on. The words that were on the bare
ground in a quiet colour, a page's blurb, the caption beside the window buttons, the footer and a
vehicle's subtitle among them, are in the body colour now. No text token's value changed.

The first drawing of the watermark had its dotted rows in the dark green, and the body text on the
bare ground read 4.81 against them with the status red at 4.34. The rows moved to the lighter teal
and the measurement is the reason.

## The first screen

Looked at before it was changed, at 375 pixels, on the live site: a header, a store switch with two
lines of explanation, the word "Inventory" and the first five of eight filters. So the inventory
now opens with **an intro strip**: one sentence, "Steven Stout's working demo: a used-vehicle
auction site on .NET and React, 100,000 vehicles, live on Azure, with its decisions written down",
and three ways on: the resume, how it is built, and the live Admin tab. It is a glass panel over the
inventory and never on top of it, every target in it is 44 pixels tall, it has a name a screen reader
can skip past, and once dismissed it stays dismissed in that browser. What it says is in
`src/lib/intro.ts` and is tested there. The resume is linked and never touched.

The phone suite holds the first screen: the brand, the sentence and the resume link are all inside
the first 812 pixels without a scroll, every target in the strip and the hamburger over it is at
least 44 pixels, the title of the inventory is under the strip and not behind it, and a dismissal
survives a reload. The store switch's two halves are 44 pixels tall on a phone as well.

**The link preview** is the first impression before the first screen: on a post, `public/og.png` is
seen before the site is. It is drawn by `docs/images/og.mjs` on the header's own gradient, with the
gold mark and the name at a size that reads when the card is four hundred pixels wide, and one line
saying what this is. The version and the count of records on it are still read from the repository,
and the tests that hold them are unchanged.

## What it cost on the wire

From the build's own output, before and after, the precheck's build of this version against
1.0.0.168's: the stylesheet went from 55.07 kB to 61.02 kB, 9.41 kB to 10.50 kB compressed, and the
script from 359.63 kB to 365.96 kB, 106.12 kB to 108.17 kB compressed. That is about three
kilobytes more on the wire for a first visit, and it is the watermark's drawing, the intro strip,
the rings and the readout. No dependency was added, no image is requested, no font was added, and
nothing on the page moves. The blur is the cost that does not show in a byte count, which is why it
is spent only on the small panels.

## What is not done, and is said here

**The writing on a chart is still small on a phone.** A chart is a 720 wide drawing, and in a 320
wide card its labels, its unit and its readout are drawn at under half size. The readout is a desk
feature in practice. This was already the next thing to fix on the Admin tab (ADR: The Admin tab, as
a product, the addendum on the look) and it still is.

**The drawings under `docs/images` keep the old palette for now**, and so do the two store colours,
for one reason: the drawings are where slate blue and brown mean "the relational store" and "the
document store", in five pictures and the records that quote them. Moving the store colours on the
page and not in the drawings would be the half move the palette's record rules out. They move
together, in their own ship.

## Addendum, 2026-09-21 (1.0.0.170): every surface, not only the Admin tab's

Steve, an hour after 1.0.0.169 went live, looking at the inventory: "Don't forget to style the
inventory and the tabs the same way as Admin. I think the background and the transparency are
missing." He was right, and the cause was a decision in this record. To save a phone the cost of a
blur, 1.0.0.169 left the hundred vehicle cards solid white, and with them the panels of a vehicle's
page, the account tab's card and the store bar, which nobody had decided at all. A page of solid
white cards over a watermark is a page with no watermark: the drawing was only visible in the
gutters, and the inventory read as the old site with a new header.

The cost that mattered was the blur, not the see-through. So the see-through went everywhere and the
blur stayed where it is cheap:

- **A vehicle card is glass without the blur**, the way the Admin tab's wide cards are. Its ground
  is white at two thirds, its border the hairline teal, its photograph solid.
- **A vehicle's page, the bid panel, the account tab's card, the store bar** and the two notices an
  empty or failed inventory shows are glass with the blur: there are few of them on a page.
- **The Account tab's title takes the gold underline** and its Back button is glass, as the Admin
  tab's are, because two tabs that look different are two applications.
- The side rail stays solid, and the documents' dialog stays solid white: it is for reading.

The worst-case arithmetic does not change. A word inside a card without a blur has the same colour
behind it at worst as one inside a card with one, and `glass.spec.ts` now holds that a vehicle card's
ground is see-through and the card itself is at full strength.

## Addendum, 2026-09-22 (1.0.2.0): every document on the Author page's panels

Steve, on reading the library after the Author page was rebuilt: "our documentation isn't formatted like the author section with the nice background and formatting". He was right, and the difference was not a style anybody chose. The Author page had a layout of its own, `layoutAuthor()`, that put its words on glass panels over the ribbon ground; every other document, the decision records among them, was a single column of markdown on the dialog's white.

**Decision: every document takes the same panels, and the same ground.** `src/lib/docLayout.ts` is twenty lines: the title and whatever comes before the first second-level heading open the page, and each second-level heading after it starts a panel that runs to the next one, which is the shape every document in this repository already has. The dialog carries its own copy of the ribbons for every document, not only for the author's, and the panels carry the words so nothing is ever read off the drawing. The panel rule itself is now one rule for both, so a change to the Author page's panels is a change to the library's.

What it does not do: it does not reflow a document, rename a heading or move a word. A document with no second-level heading is one panel, and a heading inside a code sample is escaped markup by the time this sees it, so it opens nothing.

**1.0.2.2, measured after it shipped:** a phone could still slide a document sideways, and the panels were not the cause. At 375 the README's dialog body was 375 wide and scrolled to 460, pushed by the long unbroken tokens inline code carries in prose and in lists, a file path 396 px wide in a 306 px column; code blocks were innocent, because a block keeps its sideways scroll inside its own box. A word that cannot fit now breaks (`overflow-wrap: anywhere`, which also shrinks the minimum a grid column measures), a table too wide for a phone scrolls inside itself, and `mobile.spec.ts` holds four documents to a dialog that never scrolls sideways.

## Addendum, 2026-09-23 (1.0.3.9): one consistent experience, from tokens

Steve, 2026-09-23: polish, "consistent user experience" second only to performance. So every view was read at 390, 1280 and 1440 in Chrome and WebKit before anything changed, the computed facts and not the pictures: headings, panels, buttons and pills, the focus ring on the first Tab, icons, empty states, the wording of every action and the back path. The panels were already one rule (radius, border, shadow, glass). The rest was not: three focus rings (the site's accent ring, the browser's default on the skip link, and offsets from minus two to four pixels across thirty-three rules); button heights of 28, 30, 34, 37, 39, 41, 44 and 48 for the same kinds of control, the vehicle's back button a text link where Admin's and Account's are pills; inputs on two border colours and two radii; the vehicle's title the one page heading at semibold; small icons at 14 and 16 px with strokes of 1.6, 1.8 and 2 depending on the file; badges at medium beside status pills at semibold; and "Back to inventory" on Admin and Account when they were opened from the landing page, where Back goes home.

Each is a token in `tokens.css` now (the `controls` region): one focus ring with one offset and one inset for a control clipped by its box, and the gold ring where the ground is dark; a pill height, a control height and a large one, each the thumb's target on a phone; a pill weight, a control weight and a badge weight; an input border and radius; a title weight and line; two icon sizes and one stroke, which `src/lib/icons.ts` carries for the components that draw their own SVG. The back button's label is the entry's own record of where it came from. `StyleRulesTests` rule eight holds every sheet to the ring, the offset, the heights and the title weight, `tokens.test.ts` holds the values, `icons.test.ts` holds that no component writes an icon's number itself, and `landing.spec.ts` holds the back path.

## Addendum, 2026-09-24: the operator's look

Steve, on the drawings for the Admin tab: "more iron man", "make it seem futuristic", and in the same breath "the color scheme should not change" and "leave the header and page background the same". So the treatment lives on the panels, not the page. **A panel is glass with a top rule and two corner brackets**: the dark green rule on panels and cards and the gold one on stat tiles (the landing tiles' borders, which he named), the brackets in the rule's colour at the top left and bottom right, bent round the panel's radius (his words: "rounded with no square corners"), 2 px at 18 px on a card, 14 on a rail and 10 on a tile. **A reading is small spaced capitals with tabular figures**, a label and its value on one line with a hairline under it (`Readout`). **A number that is a share of a whole is a ring** (`Ring`): a track, the value's arc from twelve o'clock, a second series inside it in gold, the number always in words inside or beside it so the arc is never the only carrier, filled from nothing on its first paint over 400 ms and not at all under reduced motion. **Every control is the site's pill group** (the store toggle's shape, `.op-seg`): a rounded border round the segments, the chosen one filled with the accent. All of it is tokens (the operator-look region of the token sheet: the rule, the bracket sizes and stroke, the readout's size, tracking and weight, the ring's three colours and its fill time, every colour one the palette already had) and one shared sheet, `src/styles/operator.css`, applied by class name, so no component draws its own bracket or ring. `operator.test.ts` holds the sheet to the tokens and the tokens to the palette. The Admin tab takes it first, with the workbench (ADR: The Admin tab, as a product, the addendum on the workbench); the rest of the site follows, a view at a time.

## Addendum, 2026-09-25 (1.0.3.20): the frost, and the ribbons back in Chrome

Steve sent two pictures of frosted glass as the model "only on the transparency": panels you can see through, where what is behind reads as soft colour and light, never as shapes, with a bright rim on the edge. So the glass keeps its share of white (42 per cent, 50 on a phone, and every contrast figure above stands on that share) and its blur goes from 7 px to 20 px with the saturation from 1.2 to 1.5, and the shadow gains a light inner rim. The blur is still spent only where it was: a vehicle card and the Admin tab's wide cards stay glass without it. `tokens.test.ts` holds the frost (a blur of 16 px or more, a saturation over one) beside the three fallbacks that turn a panel solid.

The same day he asked for "the original background image" back. The ribbons had been blank in Chrome, on 1.0.3.17 and on 1.0.3.19 alike: the page's drawing and the document dialog's copy named their gradients and filters alike, a reference finds the first element with the name, and that one was inside the closed dialog, which Chrome does not paint from; WebKit drew them, which is why the pictures in WebKit showed ribbons and the ones in Chrome did not. Each copy names its own now, and `coverage.spec` holds every page the site lists to one use of an id and every reference inside a drawing to an element of its own drawing.

## Files

- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the glass tokens and their three fallbacks, beside the palette.
- [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): every text colour against the watermark at its worst, bare and through a panel, and the fallbacks.
- [`src/components/Watermark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/Watermark.tsx) and [`src/components/Watermark.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/Watermark.module.css): the one drawing behind the page.
- [`src/components/IntroStrip.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/IntroStrip.tsx) and [`src/lib/intro.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/intro.ts): the first screen's sentence and its three links.
- [`src/lib/statTiles.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/statTiles.ts): which tiles carry a ring, and the ring as a stroke.
- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the readout's arithmetic and its words, and the fine grid.
- [`src/styles/operator.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/operator.css) and [`src/styles/operator.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/operator.test.ts): the operator's look as one shared sheet, and what holds it to the tokens.
- [`src/components/Ring.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/Ring.tsx), [`src/components/Readout.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/Readout.tsx) and [`src/lib/ring.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ring.ts): the ring and the readout, one component each.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx) and [`src/components/AdminPanel.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.module.css): the tiles, the rings, the readout and the panels of the Admin tab.
- [`tests/e2e/glass.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.spec.ts) and [`tests/e2e/glass.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.ts): nothing that is read is faded, quiet words are never on the bare ground, and the watermark is one drawing with no request behind it.
- [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts): the first screen on a phone.
- [`docs/images/og.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/og.mjs): the link preview, in the new look.

```live path=src/styles/tokens.css region=glass
```

```live path=src/components/Watermark.tsx region=*
```

```live path=src/lib/statTiles.ts region=tile-rules
```

## Addendum, 2026-09-21 (1.0.0.175): the ribbon ground

Steve saw a background injected into the live site in the browser pane and said "that is the
one": a ground that runs left to right from a soft green-grey to white, and teal and gold ribbons
sweeping in an S-curve down the left of the content, with pale gold highlight strands, two soft
star flares and twinkling gold sparks. It replaces the flat grey behind the panels and nothing
else. The panels, the words, the pictures, the header, the rail and the watermark are unchanged,
and the watermark stays in front of the ribbons.

**It is code, never an image.** The ground is one token, `--gradient-ground`, on the body. The
ribbons are one inline SVG in `Ribbons.tsx`, mounted once in the app shell beside the watermark,
fixed, hidden from assistive technology and deaf to the pointer. Their numbers are the approved
drawing's, in `src/lib/ribbons.ts`; their colours are eleven tokens, set on the gradients' stops
by the stylesheet, and with the ground's four they are on the style page under Background. They start at the rail's right edge:
the rail's width token when it is docked, its collapsed width when it is collapsed, and the
screen's edge when it is the drawer, read from the shell's own `data-rail` state rather than from
a second copy of its breakpoint.

**It is cheap to draw, and a test holds each part of that.** The whole drawing drifts on one
transform with `will-change`; inside it only opacity moves, on the sparks and the two flares.
Nothing that moves carries a blur: the ribbons and the highlight strands keep their two soft
glows and never move on their own, the flares pulse on a group whose glow sits on the group
inside it, and the sparks glow through a radial gradient. A reader who asked for less motion
gets none. Markup, data and style are under twelve kilobytes of source, about four compressed,
and they fetch nothing. `ribbons.test.ts` holds those rules against the files, `tokens.test.ts`
holds every text colour against the ground's darkest stop as well as the page grey, and the
browser suite finds one drawing behind the inventory, the Admin tab and a document, at the rail's
edge on a desk and the screen's edge on a phone, still under reduced motion.


## Addendum, 2026-09-21: the ribbons stand still

Steve: "No ribbon movement at all it should center only with CSS, we want a minimal website that looks good." Measured the evening before in Chrome with its GPU, a minute of the front page spent 6.9 s of main-thread work in every 20 s with the ribbons moving and 0.6 s with them still. The drift, the twinkle and the pulse are gone, with their keyframes and `will-change`; the flares and sparks are drawn in the one SVG with the ribbons, since nothing in it changes; the sparks keep the opacity they had for a reader who asked for less motion. The drawing is placed and centred in the content area by the stylesheet alone, from the shell's `data-rail` state. `ribbons.test.ts` now holds that nothing in the sheet or the component moves, and the browser suite finds no animation on a desk or a phone. The paragraph above on what moves describes the drawing as it was approved on 21 September and is kept as the record.

## Addendum, 2026-09-22 (1.0.1.0): showcase

The intro sentence in `src/lib/intro.ts` now reads "showcase" where it read "working demo" (Steve: "it should be showcase"). The quotations above keep the words of the day they were written.
