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
