# ADR: The palette

Status: accepted, 2026-09-02, shipped as 1.0.0.21.

## Context

The site had shipped in a navy and electric blue it inherited from the
take-home it grew out of, and by evening Steve had chosen its own: color
scheme 7 in Figma's website color schemes library, "Urban slate", five
colors in gray, brown and blue. Figma's line on it: "this color scheme
pulls from foggy cityscapes to evoke a sense of calm and sophistication.
The combination of light and dark shades creates a sense of depth and
contrast, while the overall color scheme maintains a serene and
professional aesthetic." Steve's instruction, in full: "Use this one. Make
it all match."

The five, as Figma prints them: #E9E6E7 (a light warm gray), #5E5653 (a
dark brown-gray), #7B7F8A (a slate gray), #AB978C (a warm taupe) and
#6B7C98 (a slate blue).

## Decision

Every color token in the site comes from the five, and where a shade had
to move for readability, the record beside it says which of the five it
came from and why.

- **The ground is the light gray.** The page behind the cards is #E9E6E7
  as printed; cards and the sidebar are white so the depth Figma describes
  is real. Borders are the same gray a step darker.
- **Text is the brown-gray.** Body text is #5E5653 as printed; it measures
  7.2:1 on white and 5.8:1 on the ground, past the 4.5:1 that WCAG AA asks
  of normal text. Headings and the brand use a deeper cut of the same
  brown-gray, #3F3A37, for weight.
- **Muted text is the slate gray, deepened.** #7B7F8A as printed measures
  4.0:1 on white, short of AA for small text, so muted labels use #62666F
  (5.8:1 on white, 4.6:1 on the ground) and the faintest labels #6F737E
  (4.7:1 on white). The printed slate gray survives as the icon color in
  the sidebar, where 3:1 is the bar and it clears it.
- **Actions are the slate blue, deepened.** White text on #6B7C98 as
  printed measures 4.2:1, so buttons and links use #536786 (5.7:1 with
  white text on it, 5.7:1 as link text on white). A pale tint of the
  blue, #E4E9F1, sits behind hover and current rows, and the focus ring
  and the current row's edge are the deepened blue.
- **The taupe is the brand mark.** #AB978C measures 2.8:1 on white, which
  rules it out for text; it marks the lightning bolt in the sidebar and
  the header, and the favicon, the only warm note on the page, where
  contrast rules do not apply to a decoration.
- **Status colors stay semantic.** Green, amber and red on titles,
  condition grades and the live badge carry meaning a buyer reads at a
  glance, and they are the one part of the page deliberately outside the
  five. Shadows and dimmed backdrops are the brown-gray at low opacity.

## In the code

The palette, read from this build
([`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css)):

```live path=src/styles/tokens.css region=palette
```

The sidebar's share of it:

```live path=src/styles/tokens.css region=sheet-tokens
```

The proof is
[`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts),
which reads the tokens file and holds every text and ground pair above to
WCAG AA, so a shade that fails contrast fails the build.

## Consequences

- One file changes the look of everything, because every component reads
  the tokens and none carries a color of its own. The repaint touched the
  tokens, two dimmed backdrops, two placeholder gradients, the countdown
  chip on photos, and the favicon.
- Three of the five needed a deeper cut to clear AA for text. The printed
  values still appear where they can: the ground, the body text, the
  sidebar icons, the hover tint, the brand mark.
- A future palette is the same exercise: replace the values in the region
  above, run the unit test, ship.

## Files

- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): every color token (regions palette and
  sheet-tokens above).
- [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): the contrast proof.
- [`src/components/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.module.css),
  [`src/components/DocsMenu.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.module.css),
  [`src/components/AuctionCountdown.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AuctionCountdown.module.css),
  [`src/components/VehicleCard.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleCard.module.css),
  [`src/components/VehicleImage.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleImage.module.css): the five stylesheets
  that carried a color of their own before the repaint.
- [`src/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/App.module.css) and [`src/components/BrandMark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/BrandMark.tsx):
  the brand mark in the palette's taupe.
- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the favicon.
- [`src/styles/fonts.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/fonts.css) and [`src/assets/fonts`](https://github.com/SteveStout/TheYard/tree/main/src/assets/fonts): the four Poppins faces, served by the site itself since 1.0.0.140, with the font's licence beside them.
- [`tests/e2e/fonts.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/fonts.spec.ts): no request leaves for a font host, and Poppins is the face the page paints with.

## The look, from the live site

![The inventory in Urban slate: the light gray ground, white cards and rail, brown-gray text, the taupe brand mark](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg)

![A vehicle open: the slate-blue Place bid button and Back link on the same ground](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-vehicle.jpg)

The measurements that hold the palette to WCAG AA, read from this build
([`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts)):

```live path=src/styles/tokens.test.ts region=site-palette
```

## Addendum, 2026-09-17: the type is the site's own

Steve, 9/17: the fastest site at no extra cost. Type stays Poppins; where it
comes from changed. Until 1.0.0.140 the head of the page held a stylesheet
link to fonts.googleapis.com and preconnects to it and to fonts.gstatic.com,
and every cold visit paid for both: a DNS lookup and a TLS handshake to each
host, and a stylesheet the browser would not paint without. The measurement
that opened the performance lane (`mentor\logs\lane0917-963-perflane-measure.log`,
Lighthouse against the live site on 1.0.0.139, throttled phone profile) named
that stylesheet as the page's one render-blocking resource and put its cost
at 852 ms of an estimated 1,080 ms of savings; on a desktop Chromium with a
cold cache the four woff2 files and the stylesheet were five of the thirty
requests a first visit made.

The four latin woff2 files Google served, one per weight the tokens name,
are in `src/assets/fonts` now, fetched once as Chrome received them and
kept with the font's Open Font License beside them. `src/styles/fonts.css`
declares the four faces with the same `unicode-range` and the same
`font-display: swap` the Google stylesheet carried, so a glyph outside the
latin set falls back exactly as it did, and text is readable in the fallback
face until the file lands. Vite hashes the files under `/assets`, which puts
them under the year-long cache rule every bundle file already has (ADR: Cache
headers), and the edge keeps a copy the same way it keeps the script.

What was considered and not done, with the cost of each. A system font
stack costs nothing on the wire and changes the look of every page, and the
look was Steve's decision, not a performance budget's. `font-display:
optional` would stop the swap that shows up as layout shift on a slow first
visit, at the price of a first visit that never shows Poppins at all. A
`preload` for the two most used weights would take one hop off the critical
path, and it needs the hashed file names that only the build knows, which
means a plugin or a hand-typed name that goes stale on the next build; the
stylesheet is same-origin and first in the head, so the files are discovered
early enough without it. Poppins itself covers latin, latin-ext and
devanagari; the other two subsets were never fetched by an English page and
are not carried. One place still names the Google stylesheet, and is left on
purpose: the diagram pages under `/diagrams` are HTML the server writes with
no build step to hash a file for them (ADR: Every diagram opens on its own page), and they are not
the page a visitor lands on.

`fonts.spec.ts` holds it: no request leaves the page for a Google host, and
after `document.fonts.ready` Poppins at the body weight is a loaded face.
The before and after, measured on the live site, are on the Performance
page.

## Addendum, 2026-09-21: the series colours

Two tokens were added, `--color-series-1` and `--color-series-2`, for a chart's lines when the lines
are not the two stores. The Admin tab's traffic card had drawn its slow-requests line in the warning
colour and its typical line in the success colour, and Steve read the first as an alarm while
scanning the page (ADR: The Admin tab, as a product, the addendum on the traffic card in plain
words). A series is an identity, so it needs a colour that means nothing else on the page: not a
status colour, and not a store's, because the two store colours mean the two stores on every
comparison.

No colour came onto the page with them. The first holds the accent's value, `#536786`, and the
second the heading's, `#3f3a37`, both from the five this record chose. They are tokens of their own
so that a change of palette changes two values in `src/styles/tokens.css` and touches no chart.

Measured, the way the rest of the sheet is, as graphics against the 3:1 that WCAG 1.4.11 asks:

| Token | Value | On white | On the page ground `#e9e6e7` |
| --- | --- | --- | --- |
| `--color-series-1` | `#536786` | 5.75 | 4.64 |
| `--color-series-2` | `#3f3a37` | 11.22 | 9.05 |

`tokens.test.ts` holds both figures at 3:1 or better on both grounds, that the two are told apart,
and that neither is the value of a status token.
