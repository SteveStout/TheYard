# ADR: The tweaks pass

Status: accepted, 2026-09-25. Steve approved every item on one drawn page of before and after pairs between 06:50 and 06:54 CDT ("all of this looks good"), and asked for it fast and in one push. Shipped as 1.0.3.23, with the eight fixes to the Site activity card (ADR: Site activity, and the line an address does not cross, the addendum of 25 September).

## Context

The operator's look (ADR: The glass look, its addenda of 24 and 25 September) put every page on one glass, one rule, two brackets and one face in five versions over two days. Read again the next morning with a designer's eye, the look held and a dozen details did not: glass that still read as a sheet rather than frost, a document's page that went solid white on a phone, tables that broke "Resource" into "Resou rce", a strip of Admin tiles with labels three lines tall at 390, a full ring that read as a plain circle, a store note cut with an ellipsis, a vehicle page that put the bid 2,000 px down on a phone, and charts drawn with a web grid where the rest of the tab was an instrument.

This record is the refinement, not a redesign: the palette, the header, the ribbon ground, IBM Plex Sans, the glass, the brackets and rules, the rings and the pills all stay. Nothing was added beyond the list he approved.

## What changed, and why

| Item | Before | After | Why |
| --- | --- | --- | --- |
| The glass | 42 % white, blur 20 px, saturation 1.5 | 30 % white, blur 28 px, saturation 1.6, a light rim and an inner glow | Frost you read through; the ribbons behind a panel are colour, never lines |
| Secondary ink | Two greys a step apart | One, `#4a4e57` | Over the thinner glass the old grey read 4.31 where the glass lies straight over a ribbon's teal; the ink went darker, the glass did not go back |
| A document | A white sheet over a dimmed page | A clear sheet (10 % white, blur 6 px, nothing dimmed) holding frosted reading panels (78 %, blur 28 px) | His "About Steven" picture: the page reads through the popup, the words stand on frost |
| Panels | Plain glass | A 24 px hairline grid inside glass panels only | The instrument's ruled glass; never on a tile, a pill, an input or the header |
| Charts | A dashed grid, grey axes | One teal axis line, graduation ticks, gold bracket ticks at two corners, a callout on the peak of interest, teal and gold series | Steve's flight-panel picture, the Mark VII grammar |
| Gauges | A ring with no marker | A faint track, a deep teal fill, a gold marker at the fill's end, the reading inside; graduations on This hour; bar gauges for memory and request units | A ring at 100 % read as an empty circle |
| Tables | `overflow-wrap: anywhere`, a grey header block | Words kept whole, the first column and numbers on one line, a scroller of their own, small capitals over a 2 px rule | A phone scrolls a table inside its panel and never breaks a word |
| The Admin strip | The section's question repeated on every tile, four across at 390 | Name, value row, foot; two across under 768, four above; numbers on one baseline | The rail names the section; 80 px tiles broke every label |
| The store bar | A sentence cut with an ellipsis, a ring with no count | The short form under 1440 ("SQL site · Azure SQL Database"), the whole sentence from 1440, "2/2 ready" beside the ring | No ellipsis anywhere the gate reads |
| A vehicle on a phone | Photos, specifications, condition, then the bid | Title, bid, photos, specifications, condition, seller | A buyer reads the price first; the status line appears once, in the bid panel |
| Smaller things | | Pin in the card's header row; This hour only on the Admin home, timing and traffic; the account column centred; "demo" now "showcase"; 13 and 14 px where two sizes were off the scale; the Author blurb two lines; the Welcome panel as wide as its rows; the inventory card's words on the frost; the rail's headings with a gold tick and hairline and its current row a gold edge | Each was a detail read on the live site |

The inventory's welcome banner is gone from the inventory, since the landing page now owns that copy. Its component stays in the repository, unrendered, because the glass look's record links it as the file that first said it.

## What was decided that could have gone the other way

- **The ink darkened, the glass did not thicken.** The worst case (the muted grey over 30 % glass on the ribbons' teal stop, `rgb(95, 179, 168)`, and gold stop, `rgb(201, 162, 74)`) is a test, and `#4a4e57` is the lightest grey of the old hue that holds 4.5 on both.
- **Server errors keep the danger colour.** The drawing showed them teal; a server error is a state, and this site has held since ADR: The Admin tab, as a product, that the one line a status colour may take is that one. Turned-away requests take the gold series and the callout.
- **Three rings do not take their reading inside.** The store bar's ring is 20 px, so "2/2 ready" stands beside it; a vehicle card's 28 px ring stands beside the countdown in words; the rest read inside.
- **The store bar's sentence starts at 1440, not 1280.** The pass asked for the whole sentence from 1280. With the rail open and Azure's store names it measured wider than the band there (the gate's Cosmos DB pass caught it), so the short form runs to 1440 and nothing is cut at any width.
- **The registry gauge is not in this version.** The site reads no registry figure, and reading one needs a permission its identity does not have; a gauge with nothing true to show would be a picture of a gauge.

## How it is held

- The glass, the frost, the one grey and the worst-case pairs: `tokens.test.ts`, and StyleRulesTests for every figure on the style page.

```live path=src/styles/tokens.css region=glass
```

- A table cell never breaks a word: `coverage.spec` fails any page with a cell computing `overflow-wrap: anywhere`, on every page the site lists at 390 and 1280.

```live path=src/components/AdminPanel.module.css region=tables
```

- A ring's marker and graduations: `ring.test.ts`.

```live path=src/lib/ring.ts region=ring-marker
```

- A chart has no grid, carries a legend for two series and none for one, and calls out its peak; a bar gauge puts its reading inside from 40 %: `charts.test.ts`, on the rendered drawing.

```live path=src/lib/machineChart.ts region=mark-vii
```

- The dialog's clear sheet and frosted panel: `glass.spec` and `mobile.spec`. The strip two across and on one baseline: `coverage.spec`. The vehicle's order on a phone: `mobile.spec`. No ellipsis in the store bar at 1024, 1280 and 1440: `store-toggle.spec`. Pin and This hour: `admin.spec`.

## Files

- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the glass, the dialog's sheet and page, the grid, the Mark VII marks.
- [`src/styles/operator.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/operator.css): the grid on a panel and the dialog's clear sheet.
- [`src/components/admin/charts.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/charts.tsx): the chart, its callout, and the bar gauge.
- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the graduations and the peak.
- [`src/components/Ring.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/Ring.tsx) and [`src/lib/ring.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ring.ts): the marker and the graduations.
- [`src/components/VehicleDetail.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleDetail.tsx): the order under 1024.
- [`src/components/StoreBar.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/StoreBar.tsx) and [`src/lib/stores.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/stores.ts): the short note and the count.
- [`tests/e2e/coverage.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/coverage.spec.ts): the cells and the strip, read on every page.

## Addendum, 2026-09-25 (1.0.3.24): a ring's room only where a ring is drawn

Read on the live site after 1.0.3.23 rolled: on a phone every strip tile held a ring's 44 px beside its number, drawn or not, and "under 1 ms" and "124 of 124" broke over two lines in the tiles that never draw a ring. The room is now held on the three tiles that draw one (health, pages, memory), from the first paint as before, so nothing moves when a ring arrives, and the others give the number the tile's width. `statTiles.test.ts` holds that no other tile draws a ring.

## Addendum, 2026-09-25 (1.0.3.24): tables that fit, and fills you can see through

Steve, on the live site after 1.0.3.23, of a record's table running off the side of its dialog: "this looks broken", and "as much as possible we want to eliminate horizontal scrolling that's a negative pattern especially on desktop". The tweaks pass kept a table's first column on one line, from a drawing whose first column was a short name; in the style page's rules table it is a sentence. From 1.0.3.24 every column wraps between words, a word still never breaks inside itself, an identifier (a path, an address, a digest, in the small .mono type) may break at any character as code does, and only a right-aligned number stays on one line, so a table is as wide as its panel. `coverage.spec` now fails any page at 1024 or wider on which a table scrolls sideways inside its box, and the cells rule excuses identifiers and nothing else.

And, of the activity chart: "should the bars and graphs have slightly transparent background when they're filled in", "not the lines, the fill". The stacked bands are 55 per cent with a solid top edge in their own colour, the recruiter's path and the sources' bars 60 per cent on the gauges' faint teal track where the track was grey, and a bar gauge's fill 80 per cent, at which the white reading inside it still reads 5.5.

The same morning, on a phone: "the about me page opens weird when opened from the home page". Read on the live site in Chrome and WebKit at 390: the clear sheet put the dialog's title in dark type over the page's dark teal header, and the home page's large words ghosted through between the panels. On a phone the sheet is now frosted like the reading panels, title bar included; at a desk it stays clear.

## Addendum, 2026-09-25 (1.0.3.25): the phone scan

Steve: "make sure you do a mobile scan". Read on both live sites at 390 after 1.0.3.24 rolled, in Chrome and WebKit: "124 of 124" still broke over two lines beside its ring, a timing path broke every four characters once identifiers could break anywhere and the numbers kept their line, and the activity card's three windows wrapped as one pill round two lines. A phone's strip reading is a step smaller, a table's identifier column is never narrower than a short path and the cells are tighter, and the activity card's windows are separate pills on a phone as the Admin tab's own are. A table of five columns of numbers may still scroll inside its panel on a phone, as the last resort; on a desk nothing does. `mobile.spec` holds the one-line readings and the path's width.

## Addendum, 2026-09-25 (1.0.3.26): the self-review

Steve asked for a review of the lane's own code "in the best architecture". Three readers went over the diff from 4131b45 to 1.0.3.25: one for the API and the pipeline, one for the TypeScript, one for the design system and the tests. What they found and what changed:

- **Defects a person could meet.** The phone's vehicle order was a second tree, so turning a tablet across 1024 remounted the bid panel and dropped a typed amount and a bid in flight; the order is CSS now, one tree, the bid first in it. Under 1440 a screen reader heard no store note at all, since the long form was not displayed and the short form was hidden from it. The request-units gauge held the ring's total against a rate a second; it reads the busiest minute as a rate now. The tests ring rounded 1,499 of 1,500 up to 100; it rounds down and says 100 only when nothing failed, with a per cent sign. A chart's callout read its time from the first series by the called-out series' index.
- **Two found while the review shipped.** Steve's iPhone (Chrome) on 1.0.3.25: About Steven opened from the home page sat about 100 px high, its title bar above the screen and the home page's resume tile showing under its foot. A phone's document dialog is pinned to all four edges of the screen now rather than sized to `100dvh` from the top, and the page behind it is frosted with the reading panel's own ground, so whatever the phone's toolbars do, what shows round the sheet is frost; `mobile.spec` holds the sheet edge to edge with its title on the screen after the home page was scrolled, which is as close as an emulator with no toolbars gets. And the gate's new pass at 1024 found three Admin tables scrolling sideways on a desk: the site's rail and the Admin rail left a card 460 px wide. Under 1280 the Admin rail is the drawer behind Cards, as on a phone.
- **Contrast.** White inside the gold gauge fill read 2.84, and no ink reaches 4.5 there, so a gold gauge prints its reading under the track. Body text read 3.88 over the glass on the teal stop, which the test never checked; it deepened a step in its own hue (`#5e5653` to `#524b48`, ADR-016's addendum) and the test reads every ink over both stops, read from the ribbon tokens. The desk dialog's title bar was clear over the page's dark header at about 1.6; it is frosted at every width. The glass fallbacks turned the panels solid and left the dialog's sheet, its reading panels and a phone's fuller glass see-through; all four turn together now.
- **Architecture.** The Mark VII frame is one function (`src/lib/plotFrame.ts`) and one component, where the machine charts and the activity chart each drew it; the callout is placed by `calloutFor` and the bar gauge's numbers by `gaugeMeter`, both tested. The desk breakpoint is one constant (`src/lib/breakpoints.ts`). Which tiles hold a ring's room is a field the tile builder sets, where it was a second list the panel kept in step. Markdown tables are wrapped by the renderer, so a raw HTML table is left alone. The retention on the activity card is its own field on the port, where it reused the reason a store is down, and the cost is a record, counted atomically, since the drain and every report read it at once. The collector's line and the cost sentence moved to `src/lib/activity.ts` with tests, and say "1 batch" and "this container".
- **The pipeline.** The registry prune ends green with a warning when the deploy identity cannot delete, where it went red on every deploy until the grant is made; it has a five-minute limit and matches digests as fixed strings. Tests now hold the second site's name to its own workflow and every image build to a single manifest, the condition that makes deleting untagged digests safe.
- **Dead code.** The dialog's copy of the ribbons (the `contained` prop and its styles), the tile questions, and the gauge's status tone are gone. `IntroStrip.tsx` stays, unrendered, because ADR-081 names it.
- **Held on purpose.** The info block of each inventory card keeps its blur (A12), against `.op-card`'s rule that a card is glass in its ground only; it is the words' box and not the photograph, the landing median held 99 on it, and operator.css now says so. The grid stays on the landing's section tiles, which are panels; "never on a tile" means a stat tile. One correction to the table above: the grey that read 4.31 over the teal stop was `#4d515a`, an intermediate step, not the `#5f636c` the site carried until 1.0.3.23, which read 3.26 there.

## Addendum, 2026-09-25 (1.0.3.27): the styling pass, ready to be finalized

Steve: "on Monday I want to review the styling architecture as that is ready to be finalized", "make sure you get that all ready and polished for a human review", "make sure that code is your best work". This pass changes how the styling is organized and is meant to change almost nothing a visitor sees. The three visible changes are listed at the end.

- **The token sheet has three tiers.** The palette writes raw values; the roles say what a value is for and write a hex only where the role owns the colour; the components (the rail's sheet, the Mark VII marks, the operator's rule and ring) are `var()` of a role or a `color-mix` tint of one. The sheet had written white four times, the deep teal twice, the accent three times, and it still gave the rail the old body ink after the body had moved. Every repeated colour is now written as the token it repeats, and every tint of the deep teal and of the brown-gray is mixed from its token rather than copied as an `rgba`. Two unused tokens are gone (`--color-sheet-focus`, `--color-sheet-divider`). The tests, the gate and the swatches read through `var()` to the value, with one reader in TypeScript (`hexTokens`, src/lib/contrast.ts, tested on its own) and one in `StyleRulesTests`.
- **The chart series are the Mark VII marks.** `--color-series-1` and `-2` were still the dark green and bright teal from before the tweaks pass, while every chart drew the Mark VII teal and gold; the series are those two now (`var()`), the third the printed slate gray, and the style page says so. Held to the series' own test for the first time, both read under 3.0 on the page ground (2.99 and 2.96), so both went a step deeper in their own hues: teal `#13928b`, gold `#a57c1d`, 3.8 on white and 3.1 on the page ground.
- **One width scale.** Eleven breakpoints had grown in the sheets (479, 30rem, 560, 599, 600, 601, 639, 720, 767, 900 and 1023), several a pixel apart. There are six steps now, in `src/lib/breakpoints.ts`: 480, 640, 768, 1024, 1280 and 1440, written `(min-width: N)` above a step and `(max-width: N minus 0.02)` under it, so a width under zoom cannot fall between two. The Admin tab asked its own queries through a copy of the media-query hook; it uses the shared hook and the constants.
- **Sizes, weights, corners, tracking and layers are tokens.** A chart's writing (`--chart-text-*`), a ring's reading (`--ring-text-*`), a chip's corner (`--radius-xs`), a display title's tracking (`--title-tracking`), the stacking order (`--layer-*`) and how strongly a chart's fill is laid down (`--mark-*-opacity`, as `fill-opacity` on the drawing rather than `opacity`, so no fill makes a compositing layer of its own).
- **Held by the gate.** Three new rules in `StyleRulesTests`: a colour is written once and a tint is mixed; every width is a step on the scale and a component asks only through the constants; every size, weight, corner, tracking and layer comes from the sheet. `ADR: The rules a change has to pass` carries them as one row.
- **Found on Steve's phone while it was being built, and fixed in the same version.** A vehicle's page on a phone drew the bid panel, the specifications and the seller at the width of their content, about half the screen: 1.0.3.26 made the phone order a CSS order in the desk's grid, and that grid aligns its items to the start, which in a column shrinks them. The column stretches every section now, and `mobile.spec` fails a section narrower than it. The picture of that page was in 1438's set and was not looked at; every phone picture is read by eye before this version closes. The Admin strip's details were cut to one line with an ellipsis on a phone; they take two lines like the desk's. The request-units tile no longer sets the ring's total against the free tier's rate. And a page left open across a deploy loads itself again rather than showing an error (ADR-023's addendum).
- **What a visitor sees differently.** The rail's text is the body ink (`#524b48`, where it had kept `#5e5653`). Between 600 and 639 px a phone now gets the phone layout it gets at 599, and between 721 and 767 the Author page's single column. The Account page's subheading is 17 px where it was 16.

## Addendum, 2026-09-25 (1.0.3.29): the styling pass, reviewed by a reader who had not seen it

Steve: "make sure you do 3-4 quality checks of everything". The fourth check was a reviewer given the styling pass cold, with the diff and the tree and no account of what it was meant to do beyond its claims. It found these, all now fixed:

- **The stale-page reload.** Vite's preload event was default-prevented, which made the failed import resolve to nothing, so the boundary still caught a crash, flashed the error card and reported a false error before the reload. The listener is gone (the boundary knows Vite's words too), and the boundary draws "loading the new version" from its first frame. A page that is offline is not reloaded: the same words come from a chunk that had no network, and a reload would land on the browser's offline page.
- **Tokens that nothing read.** The chart lines drew the Mark VII tokens directly, so `--color-series-1` and `-2`, which the rules guard, drew nothing; the lines draw the series now. `--radius-lg` and an unused breakpoint constant are gone.
- **Values still written twice.** The touch target's 44 px (four places), a title's gold underline (six), the gold ring round a chosen control (three), the accent hairline (four), a pill's padding (three), the page's measure (three), a drawer's width (two), and five strengths are tokens now. The gold rule reads the two gold trims as gold.
- **Rules that could pass without looking.** Rule eleven stripped every `calc()` whole and missed a last declaration with no semicolon; it now reads what is left in a calc (a multiplier, or a hairline of a pixel or two), reads the `font` shorthand, and holds opacities to tokens. Rule ten read no data file and no range syntax and let a literal `matchMedia` through; it reads the photographs' data (whose `sizes` still said 720) and fails both. Rule twelve saw only a whole six-digit hex; it now fails a colour of any length inside a value and any non-white `rgb()`, and holds the fallbacks to turning grounds solid and blurs off. The sheet's reader took a name's first hex anywhere, so `--glass-bg` read as the fallback's solid white; it now takes a name's first value, whatever it is.
- **Words that disagreed with the code**, on the style page, in the sheet's comments and in the watermark's stylesheet, where a dead rule for the rings remained.

Held on purpose, and said on the style page: `color-mix()` sets a browser floor of late 2022 (Safari 16.2, Chrome 111), below which the tints draw as nothing and the words and grounds are unchanged.
